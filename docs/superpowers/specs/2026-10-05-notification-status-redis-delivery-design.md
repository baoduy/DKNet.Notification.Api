# Notification status and durable delivery on the SlimMessageBus Redis queue — design

- **Date:** 2026-10-05
- **Status:** Approved in conversation (brainstorming), to be implemented on `feature/notification-status-redis-delivery`
- **Replaces:** ADR-0003 (in-process delivery queue). **Amends:** ADR-0002 (Redis only for idempotency).
- **New ADRs:** ADR-0012 (status tracking), ADR-0013 (delivery through the SMB Redis queue).

## 1. Goal

Callers get a `notificationId` back but cannot look it up. They also lose every queued or retry-waiting notification
when a replica stops, because delivery runs from an in-process queue.

This change:

1. Adds `GET /v1/notifications/{notificationId}` that answers `pending`, `success` or `failed` for the caller's own
   notification, for a configurable time (default 24 hours).
2. Moves delivery onto a SlimMessageBus Redis queue, so waiting notifications and retry waits survive a restart or a
   deploy. Only documented SMB features are used.
3. Changes the `POST /v1/notifications` success answer from `202` to `200`.

## 2. Decisions taken (with the user)

| # | Decision |
|---|---|
| D1 | Public status values: `pending`, `success`, `failed`. **Skipped is shown as `failed`**; the skip reason is never shown. |
| D2 | Reading a status needs the existing `notifications.send` permission (ADR-0007). |
| D3 | The lookup is scoped to the caller: another caller's id answers 404, the same as an unknown or expired id. |
| D4 | The status response is `{ notificationId, idempotencyKey, status }`. |
| D5 | Delivery uses the SMB Redis provider queue (Redis list, `RPUSH`/`LPOP`, at-most-once per pop). Retries are re-published to the queue, never held in memory. |
| D6 | Without `ConnectionStrings:Redis`, Development and Testing use the SMB memory provider (non-blocking publish) for delivery and the in-memory `IDistributedCache` for status. Every other environment refuses to start, as idempotency does today. |
| D7 | `503 QUEUE_FULL` stays. The limit is `Delivery:QueueCapacity` for the **whole service**, checked with `LLEN` before publishing (approximate). The memory fallback has no limit. |
| D8 | Queued messages hold the recipient and the rendered body as is (no encryption). They are protected by Redis access control and TLS, as idempotency records are. |
| D9 | `POST /v1/notifications` answers `200 OK { notificationId }` for Queued and Skipped. No `Location` header. |
| D10 | A message that is not yet due is re-published to the tail at once, then the consumer pauses `min(remaining, 1 s)`. It is never held for its whole wait. |

## 3. Verified SlimMessageBus 3.5.0 behaviour

Read from the `3.5.0` tag source and docs:

- **Redis queue consumer** (`RedisListCheckerConsumer`): one loop per bus does `LPOP`, awaits the handler, and pauses
  `QueuePollDelay` (1 s) once the list has been empty for `QueuePollMaxIdle` (3 s). **A handler exception is logged and
  the message is dropped.** So the consumer must catch everything itself.
- **`Instances(n)`** wraps the processor in `ConcurrentMessageProcessorDecorator` (fire-and-forget after a semaphore):
  up to n messages are held in process. This design uses `Instances(1)`.
- **Stop order:** `MessageBusBase.Stop()` awaits every consumer's `Stop()` (which awaits the loop) before destroying
  consumers. `Publish` refuses only once the bus is disposed (`AssertActive` checks `IsDisposed`). So a handler that
  sees its cancellation token fire can still publish.
- **Hybrid bus** is built in since 2.0: child buses route by message type; a request type has exactly one handler.
- **Memory provider** `Publish` blocks until the consumer ends by default; `EnableBlockingPublish = false` runs the
  consumer in the background.
- The Redis provider needs a message serializer: `SlimMessageBus.Host.Serialization.SystemTextJson`.

## 4. Architecture

```
POST /v1/notifications ─► SendNotification ─(Mediator child bus, memory, unchanged)─► SendNotificationService
                                                                                         │ validate, render
                                                                                         │ backlog full? → 503
                                                                                         │ status := pending
                                                                                         ▼
                                                     DeliverNotification ─(Delivery child bus)─► Redis list "notification-delivery"
                                                                                                         │ LPOP (any replica)
GET /v1/notifications/{id} ─► GetNotificationStatus ─(Mediator)─► NotificationStatusStore ◄─ success | failed
                                                                   (IDistributedCache)            │
                                                                                            DeliveryConsumer (Instances 1)
```

### Child buses

- **Mediator** — memory provider, as today (ADR-0011). Carries `SendNotification` and the new `GetNotificationStatus`.
- **Delivery** — carries `DeliverNotification` on the queue `notification-delivery`.
  - Redis provider with the SystemTextJson serializer when `ConnectionStrings:Redis` is set.
  - Otherwise (Development and Testing only) the memory provider with `EnableBlockingPublish = false`.

### Parts

| Part | Layer | Job |
|---|---|---|
| `DeliverNotification` | AppServices | The queued message (§5). |
| `DeliveryConsumer` (`internal sealed`, `IConsumer<DeliverNotification>`) | AppServices | One attempt per message (§6). Replaces `DeliveryWorker`'s loop. |
| `NotificationStatusStore` | AppServices | Reads and writes status records over `IDistributedCache` (§7). |
| `DeliveryBacklog` | AppServices | `HasRoom()`: `LLEN notification-delivery < QueueCapacity` on Redis; always `true` without Redis. Also feeds the queue-length gauge. |
| `GetNotificationStatus` + handler | AppServices | The mediator request behind the `GET` route. |
| `Notification.Resume(...)` | Domains | Rebuilds a notification from a queued message: `Queued` when no attempt was made, `RetryWaiting` otherwise. Keeps `StartAttempt`'s 3-attempt limit in the domain. |
| `GET /v1/notifications/{notificationId:guid}` | Api | In the `NotificationsV1Endpoint` group, so `notifications.send` covers it. |
| Removed | — | `DeliveryQueue`, `DeliveryWorkerHost`, `QueuedNotification`, the worker loop and its in-memory retry timers. |

Unchanged: the senders (`SmtpEmailSender`, `GraphEmailSender`, `TeamsWebhookSender`) and `IDeliverySender`, the
renderers, the template catalogue, idempotency, sign-in, and the error mapping.

## 5. The queued message

```csharp
public sealed record DeliverNotification(
    int SchemaVersion,                 // 1. Fields are only ever added.
    Guid NotificationId,
    string TemplateId,
    string Channel,
    string CallerId,
    string? IdempotencyKey,
    DateTimeOffset AcceptedAt,
    string TraceId,
    string? EmailAddress,              // personal data
    string? TeamsDestination,          // the destination NAME, never the webhook URL
    string Subject,
    string Body,                       // personal data
    BodyFormat Format,
    int AttemptsMade,                  // 0 when first queued
    DateTimeOffset NotBefore);         // the next attempt starts no sooner
```

Compatibility rule (in ADR-0013): a field is only ever added, as optional. A removal or rename takes two releases. A
message that cannot be deserialized is dropped by SMB before our code runs; its status stays `pending` until it
expires.

## 6. Flows

### Accepting (`SendNotificationService`, the existing step 9)

1. Validate and render, as today.
2. `DeliveryBacklog.HasRoom()` is false → reject with `QUEUE_FULL` (503, `Retry-After: 30`). Nothing is written.
3. Write status `pending`.
4. Publish `DeliverNotification` with `AttemptsMade = 0`, `NotBefore = now`.

- Skipped → write status `failed`, publish nothing.
- Rejected (400) → write nothing (no id is ever returned).
- A publish that throws after `pending` was written answers 500. The record is unreachable: no id was returned, and a
  retry after 30 s runs afresh with a new id.

### Delivering (`DeliveryConsumer.OnHandle`, one message)

```
NotBefore > now ─► re-publish unchanged ─► pause min(NotBefore - now, 1 s) ─► return
otherwise:
  Resume ─► StartAttempt ─► sender.SendAsync
    success                               ─► status success, log NotificationDelivered, metric
    permanent, or transient on attempt 3  ─► status failed,  log NotificationFailed, metric
    transient with attempts left          ─► re-publish AttemptsMade+1,
                                              NotBefore = now + (Retry-After ?? RetryDelaysSeconds[attempt-1])
    host stopping during the attempt      ─► re-publish with AttemptsMade unchanged (log NotificationRequeued)
    any other exception                   ─► status failed (SMB would otherwise drop it)
    no sender for the channel / Teams destination removed since acceptance ─► failed (existing UnexpectedError path)
```

- `now` and the due check read the injected `TimeProvider`. Parsing a `Retry-After` date stays on the real clock
  (AGENTS.md).
- A cut-off attempt is not counted: a run of deploys must not use up the 3 attempts. A duplicate is possible if the
  provider had already taken the message (already accepted by the old ADR-0003).
- The "not due" pause bounds a list made only of not-due messages to about one round trip per second per replica,
  and the message is already back in Redis before the pause, so a crash during it loses nothing.

## 7. Status records

- Key: `status:{callerId}:{notificationId}` in `IDistributedCache` (instance name `SharedConsts.ApiName`, as today).
- Value: JSON `{ notificationId, idempotencyKey, status }`, with `status` the internal value `Pending`, `Success` or
  `Failed`. The API writes it in lower case.
- Expiry: `Notifications:Status:RetentionHours` (default 24, 1–168), set on each write, so it counts from the last
  write.
- A failed write never fails the call or the delivery. It logs `NotificationStatusWriteFailed` (warning, ids only) and
  continues.
- No personal data in a status record.

| Public `status` | Internal states | Final |
|---|---|---|
| `pending` | Queued, Delivering, RetryWaiting | No |
| `success` | Delivered | Yes |
| `failed` | Failed, Skipped | Yes |

Only final values are written after `pending`, and `pending` is written before the publish, so a status never goes
backwards.

## 8. API contract

### `POST /v1/notifications` (changed)

- Success: `200 OK { "notificationId": "…" }` for Queued and Skipped.
- `503 QUEUE_FULL`: the delivery list holds `Delivery:QueueCapacity` notifications for the whole service.
- For 4 hours after the release, idempotency records kept before it replay `202`. Release notes tell callers to accept
  both until then.
- The Queued/Skipped rule in `03-integration.md` becomes: "The 200 is the same for Queued and Skipped. The status tells
  delivered from not delivered, and never says why."

### `GET /v1/notifications/{notificationId:guid}` (new)

```http
HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: no-store

{ "notificationId": "8c7e0f3a-…", "idempotencyKey": "order-42-welcome", "status": "pending" }
```

| Status | `errors[].code` | When |
|---|---|---|
| 200 | — | A record exists for this caller and id. |
| 401 / 403 | — | As for `POST`. |
| 404 | `NOTIFICATION_NOT_FOUND` | Unknown, another caller's, or expired. |
| 404 | — | The id is not a GUID (route constraint). |
| 429 | — | The existing rate limit. |
| 500 | — | Redis cannot be read. |

`idempotencyKey` is the `Idempotency-Key` header of the accepting call, read by the endpoint and passed in
`SendNotification`.

## 9. Configuration

| Setting | Default | Note |
|---|---|---|
| `Notifications:Status:RetentionHours` | 24 | 1–168. |
| `Notifications:Delivery:QueueCapacity` | 1,000 | Now for the whole service. |
| `Notifications:Delivery:MaxAttempts`, `RetryDelaysSeconds` | 3, `[5, 30]` | Unchanged meaning. |
| `ConnectionStrings:Redis` | — | Now also delivery and status. Required outside Development and Testing. |

Packages (central, 3.5.0): `SlimMessageBus.Host.Redis`, `SlimMessageBus.Host.Serialization.SystemTextJson`.

## 10. Quality

- **Loss:** waiting messages and retry waits survive a deploy. A hard crash loses at most the one message a replica
  holds. Redis data loss loses the queue. Duplicates are possible after an ambiguous timeout or a crash after the
  provider took the message.
- **Logs:** new `NotificationStatusWriteFailed` (warning) and `NotificationRequeued` (debug). Existing delivery events
  keep their fields and trace links.
- **Metric:** the queue-length gauge reads `LLEN` when observed (service-wide backlog); 0 on the memory fallback.

## 11. Tests

Business behaviour only (AGENTS.md).

- **Domains:** `Notification.Resume` (Queued vs RetryWaiting, no 4th attempt).
- **`SendNotificationService`:** `pending` before publish; Skipped → `failed`, no publish; Rejected → nothing written;
  full backlog → `QUEUE_FULL`, nothing written; idempotency key carried.
- **`DeliveryConsumer`:** not due → re-published unchanged; success; transient → `AttemptsMade+1` and `NotBefore` from
  the configured delay or `Retry-After`; 3rd transient → `failed`; permanent → `failed`; unexpected exception →
  `failed`; cancellation mid-attempt → re-published unchanged; no sender → `failed`.
- **`NotificationStatusStore`:** caller-scoped key; expiry from settings; a failed write does not throw.
- **Routes:** `GET` 200 own id, 404 unknown, 404 other caller, 404 non-GUID; `POST` answers 200.
- **BDD on real Redis:** status `pending` → `success`; `failed` after 3 attempts; a host stopped during a retry wait and
  a new host delivers; `QUEUE_FULL` against a list at capacity.
- Existing tests: `DeliveryWorkerTests` becomes `DeliveryConsumerTests`; `202` assertions become `200`.

## 12. Documents to change

- `docs/architect/adr/0012-notification-status-tracking.md` (new), `0013-delivery-through-slimmessagebus-redis-queue.md`
  (new, supersedes 0003), `0002` amended, `0003` marked superseded.
- `docs/architect/01-scope.md`, `02-domain.md`, `03-integration.md`, `04-data.md`, `05-quality.md`, `README.md`.
- `docs/operator-guide.md`, `AGENTS.md`.
- Diagram JSON sources whose flow changed (`runtime`, `notification-lifecycle`, the send sequences). Regenerating the
  SVGs is out of scope for this change; the JSON sources are updated.

## 13. Out of scope

- A dead-letter list or a replay tool for failed notifications.
- Encrypting queued messages.
- Delivery through Azure Service Bus (remains the upgrade path if at-least-once is needed).
- Listing notifications, or status history.
