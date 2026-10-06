# Read notification status

Read the public outcome of a notification accepted for the same backend caller.

This page describes code at commit `362962797612943392d6ecb73686eed9c742a574`.

## 📖 Overview

- A caller can poll the id returned by a send call to see `pending`, `success` or `failed`.
- Status is scoped to the caller claim in the bearer token; another caller receives the same `404` as an unknown id.
- A skipped call is immediately `failed`, while a queued delivery is `pending` until an attempt succeeds or ends permanently.
- Each status write starts a new retention period; after expiry the lookup returns `404`.

Backend services that submitted the notification call this feature.

## 🏢 Business domain

The public status is a compact view of Notification Delivery. It does not reveal the provider's reply or the internal delivery state.

| Term | Meaning | In the code |
|---|---|---|
| Caller | Backend application identified by `client_id`, `azp` or `appid` | `CallerIdentity` |
| Notification id | Id returned by an accepted send | `NotificationStatusRecord.NotificationId` |
| Public status | `pending`, `success` or `failed` | `NotificationOutcome`, `NotificationsV1Endpoint.Status` |

| Rule | Enforced by | A caller who breaks it gets |
|---|---|---|
| Caller must have `notifications.send` and a caller claim | `SendPermission` on `NotificationsV1Endpoint` | Authentication or authorization refusal |
| Only the matching caller can read a record | `NotificationStatusStore.KeyOf`, `GetNotificationStatusHandler` | `404 NOTIFICATION_NOT_FOUND` |
| Id must be a GUID and have an unexpired record | Route constraint, `GetNotificationStatusHandler` | `404` |

## 🚀 Quick Start

First [send a notification](send-notification.md#-quick-start) and copy its `notificationId` into `NOTIFICATION_ID`. Use a bearer token for the same caller with the `notifications.send` permission. `NotificationStatusRouteTests` sends this request with the same caller after POST:

```sh
NOTIFICATION_ID=0199c1ab-0000-7000-8000-000000000001
curl -i "http://localhost:5000/v1/notifications/${NOTIFICATION_ID}" \
  -H 'Authorization: Bearer <token>'
```

The response is `200` with `{"notificationId":"<guid>","idempotencyKey":"order-42","status":"pending"}` while queued; a fast delivery can already have changed the last field to `success` or `failed`.

## 🔄 End-to-end flow

![The status endpoint authenticates the caller, sends GetNotificationStatus through the in-memory bus, reads the caller-scoped cache key and returns 200 or 404.](../diagrams/read-notification-status.sequence.svg)

The read has no transaction with delivery. `NotificationStatusStore.ReadAsync` sees whichever write Redis has committed when it runs; a cache outage can fail the read. `Cache-Control: no-store` stops response caching, not expiry of the underlying record.

![Public status starts pending when queued and ends success or failed; a skipped notification starts failed while a rejected call has no record.](../diagrams/notification-status.lifecycle.svg)

The diagram shows only writes made by `SendNotificationService` and `DeliveryConsumer`. A transient failure with attempts remaining republishes the message and leaves status `pending`.

## 🔌 Endpoints

| Verb | Path | Purpose | Auth |
|---|---|---|---|
| GET | `/v1/notifications/{notificationId}` | Read caller's status | Bearer caller with `notifications.send` |

### `GET /v1/notifications/{notificationId}`

Read a caller scoped status record without revealing whether another caller owns an id.

- **Auth:** `SendPermission` requires the caller claim and `notifications.send`.
- **Request:**

| Field | Type | Required | Rules | From |
|---|---|---|---|---|
| `notificationId` | GUID | yes | Id from a `200` send response | route |

**Response:** `200 OK`, with `Cache-Control: no-store`:

```json
{"notificationId":"0199c1ab-0000-7000-8000-000000000001","idempotencyKey":"order-42","status":"pending"}
```

`idempotencyKey` can be `null` in the stored record shape. The returned `status` is one of `pending`, `success` or `failed`.

| Status | Code | When |
|---|---|---|
| 401 or 403 | Authorization result | Missing/invalid identity or permission |
| 404 | `NOTIFICATION_NOT_FOUND` | Unknown, other caller's or expired id |
| 404 | — | Route segment is not a GUID |
| 429 | — | Configured rate limiter refuses the call |
| 500 | — | Cache read fails |
| 504 | — | Configured request timeout expires |

The Quick Start `curl` command is the runnable example.

## 🗃️ Data model

### `status:{callerId}:{notificationId}` — distributed cache key

One logical key holds the public status for one caller and notification. `NotificationStatusStore.KeyOf` constructs it; the Redis cache instance can add a physical prefix.

| Field | Required | Purpose |
|---|---|---|
| `NotificationId` | yes | Match the id returned by POST |
| `IdempotencyKey` | no | Correlate this result with the accepting call |
| `Status` | yes | Report the current public outcome |

`NotificationStatusStore.WriteAsync` sets absolute expiry to `Notifications:Status:RetentionHours` after each write, 24 hours by default, with a valid range of 1–168 hours. Expiry removes the key. A send refusal creates no key. A failed write is logged and dropped, so an accepted call can have no readable status. See the [send data model](send-notification.md#-data-model) for the delivery list.

| Value | Meaning | Reached by | Next |
|---|---|---|---|
| `pending` | Queued, in an attempt or waiting to retry | `SendNotificationService.QueueAsync` | `success` or `failed` |
| `success` | Provider accepted the message | `DeliveryConsumer.EndAsync` | terminal |
| `failed` | Skipped or delivery exhausted/failed | `SendNotificationService.SkipAsync` or `DeliveryConsumer.EndAsync` | terminal |

## 🌐 Downstream systems

| System | Direction | How | What for | When it is down |
|---|---|---|---|---|
| Redis | We call it | `IDistributedCache` via `NotificationStatusStore` | Read the caller's status | Read throws and the request can fail |

Development and Testing without Redis use the memory cache; see the [operator guide](../operator-guide.md#redis).

## ⚙️ Configuration reference

| Key | Type | Required | Default | Rules | Secret | Takes effect | Effect |
|---|---|---|---|---|---|---|---|
| `Notifications:Status:RetentionHours` | integer | yes | 24 | 1–168 | no | startup | Absolute cache expiry after each status write |

Shared Redis and auth settings are in the [operator guide](../operator-guide.md). The chart maps `:` to `__` in environment variable names at pod startup.

## ⚠️ Errors & limits

A `404` intentionally does not distinguish an unknown id, another caller's id or an expired record. The error body has `errors[].code` set to `NOTIFICATION_NOT_FOUND` for a GUID lookup. A missing status does not prove delivery failed: writes are best effort and an item taken from the Redis list can be lost on a hard crash. A `pending` record can outlive a lost queue item until its expiry. Poll with the same caller claim; this route does not return provider details or a skip reason.

## 🔗 Related features

- [Send a notification](send-notification.md) to obtain the id and understand queued versus skipped acceptance.
- [Deployment guide](../deployment.md) for post-deploy checks of both routes.

## ❓ Open questions

| Question | Why it matters | Checked | Who can answer |
|---|---|---|---|
| What polling interval and service objective should callers use? | Controls load and timeout expectations. | Endpoint, settings and tests | Product owner |
| What status retention runs in each deployed environment? | The code default does not prove the live expiry. | `NotificationStatusSettings`, chart values, workflows | Operator |
| What recovery procedure applies to a permanently pending status after an item is lost? | Determines caller and operator action. | `DeliveryConsumer`, operator guide | Service owner |
