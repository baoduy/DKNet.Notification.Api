# Send a notification

Submit one registered template and its parameters for delivery to an email recipient or a Teams destination.

This page describes code at commit `362962797612943392d6ecb73686eed9c742a574`.

## 📖 Overview

- A caller chooses a registered template, channel and parameter values; rendering happens once before queuing.
- The API returns an id for both queued and skipped calls. A skipped call delivers nothing and immediately has public status `failed`.
- A valid email or Teams call enters one shared delivery queue when that channel and template version are configured.
- The same caller can reuse an `Idempotency-Key` to replay an accepted response without sending again during the key's retention window.

Backend services with the `notifications.send` permission call this feature.

## 🏢 Business domain

Notification Delivery turns a template version into one rendered message for one channel. A notification is a delivery request, not a domain event.

| Term | Meaning | In the code |
|---|---|---|
| Notification | One accepted request for one message | `Notification` |
| Template version | Content for one channel | `NotificationTemplate`, `TemplateVersion` |
| Channel | Requested delivery route, `email` or `teams` when supported | `SendNotificationService` |
| Recipient | Email address or configured Teams destination | `EmailRecipient`, `TeamsRecipient` |
| Skipped | Accepted call with no delivery because the route is unavailable | `SendNotificationService.SkipAsync` |
| Rejected | Refused call with no returned notification id | `SendNotificationBodyFilter`, `SendNotificationService.Reject` |

| Rule | Enforced by | A caller who breaks it gets |
|---|---|---|
| Body fields and parameter bounds must pass validation | `SendNotificationValidator`, `SendNotificationBodyFilter` | `400 INVALID_REQUEST`; oversized body gets `413` |
| Template id must be registered | `SendNotificationService.SendAsync` | `400 TEMPLATE_NOT_FOUND` |
| Email `to` or Teams `teamsDestination` must be valid | `SendNotificationService.SendEmailAsync`, `SendTeamsAsync` | `400 RECIPIENT_MISSING` or `RECIPIENT_INVALID` |
| Every template token must have a parameter | `EmailRenderer`, `TeamsRenderer` | `400 PARAMETER_MISSING` |
| Queue length must be below configured capacity | `SendNotificationService.QueueAsync` | `503 QUEUE_FULL`, `Retry-After: 30` |

## 🚀 Quick Start

Get a bearer token for this API with a caller claim (`client_id`, `azp` or `appid`) and the `notifications.send` permission in `scp`, `scope` or `roles`. The local Development profile disables authorization; use an empty token placeholder only there. Configure SMTP, Graph or Teams as described in the [operator guide](../operator-guide.md) before expecting delivery.

This request body comes from `NotificationStatusRouteTests`; the header set follows `SendScenario.SendUnrecordedAsync`:

```sh
curl -i http://localhost:5000/v1/notifications \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: order-42' \
  --data '{"channel":"email","templateId":"account-opened","parameters":{"to":"jane@example.com","customerName":"Jane","accountNumber":"12345"}}'
```

An accepted call returns `200` and `{"notificationId":"<guid>"}`; the id varies. [Read its status](notification-status.md) to learn whether it was delivered or skipped.

## 🔄 End-to-end flow

![The send endpoint validates the caller, body and key, then checks the template and queue, writes pending status, publishes DeliverNotification to the Redis list, and a consumer attempts delivery.](../diagrams/send-notification.sequence.svg)

`SendNotificationService.QueueAsync` writes `pending` before publishing so a fast consumer's final write can win. The write is best effort: a cache failure can leave a queued notification with no readable status. A publish failure can answer `500` after the pending write. Queue capacity is based on a list length read, so concurrent calls can exceed it slightly. No database transaction spans these operations.

![The notification public status starts pending for a queued call and ends success or failed; a skipped call starts failed, while a rejected call has no status.](../diagrams/notification-status.lifecycle.svg)

Only `SendNotificationService` and `DeliveryConsumer` write these public values. A retry keeps `pending`; there is no public retry state. The design's internal lifecycle has more states than this stored status.

![A caller posts a notification, receives its id, then polls the status route as the same caller until success or failed.](../diagrams/send-then-poll.sequence.svg)

Polling reads a cache record, not the provider. A lost record, expiry or another caller's id returns the same `404`; polling cannot recover a delivery result after that.

## 🔌 Endpoints

| Verb | Path | Purpose | Auth |
|---|---|---|---|
| POST | `/v1/notifications` | Submit a notification | Bearer caller with `notifications.send` |

### `POST /v1/notifications`

Submit a rendered message for delivery or record a skipped call when its channel is unavailable.

- **Auth:** `SendPermission` requires a caller claim and `notifications.send`; production chart enables bearer authorization.
- **Idempotency:** `Idempotency-Key` is required. A replay by the same caller and route within four hours returns the stored accepted response; a refused call is held briefly rather than cached as success.
- **Concurrency:** This creates a new notification; no version or `If-Match` applies.

| Field | Type | Required | Rules | From |
|---|---|---|---|---|
| `channel` | string | yes | 1–50 characters; matched without case | body |
| `templateId` | string | yes | 1–100 characters; registered id matched exactly | body |
| `parameters` | string map | yes | Up to 50 entries; key 1–64 letters, digits or underscore; value at most 4,000 characters | body |
| `parameters.to` | string | for email | Exactly one address, at most 254 characters | body |
| `parameters.teamsDestination` | string | for Teams | 1–64 lowercase letters, digits or hyphen; configured destination | body |
| `Idempotency-Key` | string | yes | 1–255 letters, digits, hyphen or underscore | header |

**Response:** `200 OK`, including skipped calls:

```json
{"notificationId":"0199c1ab-0000-7000-8000-000000000001"}
```

| Status | Code | When |
|---|---|---|
| 400 | `INVALID_REQUEST` | Malformed or invalid body |
| 400 | `TEMPLATE_NOT_FOUND`, `RECIPIENT_MISSING`, `RECIPIENT_INVALID`, `PARAMETER_MISSING`, `MESSAGE_TOO_LARGE` | Template, recipient, rendering or Teams size refusal |
| 400 | Idempotency filter error | Missing or invalid `Idempotency-Key` |
| 401 or 403 | Authorization result | Missing/invalid identity or permission |
| 413 | — | Body exceeds 65,536 bytes |
| 429 | — | Configured rate limiter refuses the call |
| 503 | `QUEUE_FULL` | Redis list reached configured capacity |
| 500 | — | Backlog read or publish fails, including Redis outage |
| 504 | — | Configured request timeout expires |

The Quick Start `curl` command is the runnable example.

## 🗃️ Data model

### `notification-delivery` — Redis list

Each list item is a serialized `DeliverNotification` containing one rendered message waiting for an attempt.

| Field | Required | Purpose |
|---|---|---|
| `SchemaVersion`, `NotificationId` | yes | Read the queued shape and tie it to the caller's id |
| `TemplateId`, `Channel`, `CallerId` | yes | Route and attribute the attempt |
| `IdempotencyKey`, `AcceptedAt`, `TraceId` | key optional | Preserve call context and elapsed time |
| `EmailAddress` or `TeamsDestination` | one | Name the delivery target |
| `Subject`, `Body`, `Format` | yes | Deliver the once rendered message |
| `AttemptsMade`, `NotBefore` | yes | Enforce retries and wait time |

There is no per-item TTL. The consumer pops an item; it republishes a not-yet-due item, a retry, or an attempt interrupted by graceful shutdown. After success or terminal failure it is removed. A crash after a pop can lose that item while status remains pending. The list is shared when Redis is configured; Development and Testing without Redis use a memory delivery topic.

### `status:{callerId}:{notificationId}` — distributed cache key

One record gives the caller a public status; `NotificationStatusStore.KeyOf` builds the logical key. The Redis cache configuration may prefix the physical key.

| Field | Required | Purpose |
|---|---|---|
| `NotificationId` | yes | Match the returned id |
| `IdempotencyKey` | no | Correlate with the accepting call |
| `Status` | yes | Expose pending, success or failed |

`NotificationStatusStore.WriteAsync` sets absolute expiry to `Notifications:Status:RetentionHours` from each write, 24 hours by default. Expiry removes the record; a final write restarts its lifetime. Rejected calls write no record. There is no relational schema or migration.

## 📣 Events

The API publishes a `DeliverNotification` bus message to the `notification-delivery` Redis list after writing pending status. `DeliveryConsumer` consumes it; the service publishes no domain event for downstream services.

| Event | Raised when | Payload | Transport | Consumers | Ordering | Duplicates | On failure |
|---|---|---|---|---|---|---|---|
| `DeliverNotification` | Queue admission succeeds | Fields in the list shape above | SlimMessageBus Redis list, memory topic in local fallback | `DeliveryConsumer` | List order, with retries requeued | An ambiguous provider timeout may cause repeat delivery | Consumer retries transient failures up to configured maximum; failed republish ends status failed when possible |

## 🌐 Downstream systems

| System | Direction | How | What for | When it is down |
|---|---|---|---|---|
| Redis | We call it | Cache and SlimMessageBus list | Idempotency, queue, status | Send can fail with `500`; status reads can fail |
| SMTP provider | We call it | `SmtpEmailSender` | Deliver email when selected | Transient failure retries; permanent failure ends failed |
| Microsoft Graph | We call it | `GraphEmailSender` | Deliver email when selected | HTTP and sign-in failures follow delivery retry classification |
| Teams Workflows | We call it | `TeamsWebhookSender` | Deliver Teams cards | Transient HTTP failure retries; permanent failure ends failed |

Addresses and credentials are in the [operator configuration reference](../operator-guide.md), not per request.

## ⚙️ Configuration reference

| Key | Type | Required | Default | Rules | Secret | Takes effect | Effect |
|---|---|---|---|---|---|---|---|
| `Notifications:Delivery:QueueCapacity` | integer | yes | 1000 | 1–100,000 | no | startup | Maximum approximate Redis list length before `QUEUE_FULL` |
| `Notifications:Delivery:MaxAttempts` | integer | yes | 3 | 1–3 | no | startup | Maximum delivery attempts |
| `Notifications:Delivery:RetryDelaysSeconds` | integer list | yes | 5, 30 | Two values, each 1–300 seconds | no | startup | Delay before attempts 2 and 3 |

Shared Redis, channel, status and auth settings live in the [operator guide](../operator-guide.md). ASP.NET Core environment variables replace `:` with `__`; the chart supplies them at pod startup.

## ⚠️ Errors & limits

Problem responses carry an `errors` array with a code and field; the code does not echo parameter values. A successful HTTP response does not certify provider acceptance. A skipped call is indistinguishable in the `200` body and has public status `failed`; inspect `NotificationSkipped` logs for its reason. Personal data in a queued recipient and rendered body remains in Redis until the item is consumed. A retry with a new idempotency key may send a second notification.

## 🔗 Related features

- [Read notification status](notification-status.md) after receiving an id to learn the public outcome.
- [Deployment guide](../deployment.md) when preparing Redis and verifying a channel after release.

## ❓ Open questions

| Question | Why it matters | Checked | Who can answer |
|---|---|---|---|
| What is the production recovery target for lost queue items or Redis data? | Determines caller retry and incident response. | `DeliveryConsumer`, chart, operator guide | Service owner |
| What queue capacity and retry delays run in each deployed environment? | Defaults do not prove production values. | `DeliverySettings`, chart values, workflows | Operator |
| Which team handles delivery failures and provider incidents? | Directs operational escalation. | Repository documentation | Project owner |
| The design draws Delivering → Retry Waiting, but `DeliveryConsumer` republishes without calling `Notification.WaitForRetry`. Should the aggregate record that transition? | Keeps the design lifecycle aligned with code. | `DeliveryConsumer`, `Notification`, design lifecycle | Service architect |
