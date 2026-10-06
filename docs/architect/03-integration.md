# 03 — Integration

## Context map

| Neighbour | Direction | How they talk |
|---|---|---|
| Backend callers (DKNet.Accounts.Api is a likely first) | Caller → this service | HTTPS REST, Entra ID bearer token |
| Microsoft Entra ID | This service → Entra ID | OpenID Connect metadata and signing keys, read to validate tokens. With the Graph sender, also a client credentials token request for Graph |
| Redis | This service → Redis | Redis protocol, for idempotency records, the delivery queue and notification status records |
| SMTP provider | This service → provider | SMTP with STARTTLS or TLS, authenticated. Only when `Sender` is `Smtp` |
| Microsoft Graph | This service → Graph | HTTPS POST to `sendMail` with an Entra ID bearer token. Only when `Sender` is `Graph` (ADR-0009) |
| Microsoft Teams Workflows webhook | This service → webhook | HTTPS POST of an Adaptive Card message |
| DKNet packages (DKNet repo) | This service → packages | NuGet package references, in process |
| DKNet.Templates | One-time, at scaffold | `dotnet new dknet-minimal`; no runtime link |

This service calls no other DKNet service. No DKNet service is called by it. No library depends on it.

![DKNet Notification sits between backend callers and its delivery targets — the SMTP provider or Microsoft Graph for email, one per deployment, and Teams Workflows webhooks — and depends on Entra ID for tokens, Redis for idempotency records, the delivery queue and status records, and DKNet packages at build time.](diagrams/context-map.svg)

## Exposed API

| Verb | Path | Purpose | Auth |
|---|---|---|---|
| POST | `/v1/notifications` | Accept one notification for one channel. | Bearer token with scope or app role `notifications.send` (ADR-0007) |
| GET | `/v1/notifications/{notificationId}` | Read the status of one notification the caller accepted: `pending`, `success` or `failed`. | Bearer token with scope or app role `notifications.send` (ADR-0007) |
| GET | `/healthz` | Liveness: the process is up. | Anonymous |

The scaffold's OpenAPI and Scalar pages stay behind its `EnableSwagger` flag, which is off by default.

### `POST /v1/notifications`

Headers:

- `Authorization: Bearer <token>` — required.
- `Idempotency-Key: <key>` — required. 1 to 255 characters: letters, digits, `-` and `_` (the package defaults).
- `Content-Type: application/json` — required. The body is at most 64 KB.

Body fields:

| Field | Type | Required | Rules |
|---|---|---|---|
| `channel` | string | Yes | 1 to 50 characters. Matched without case. Any value is accepted here; an unknown one ends Skipped. |
| `templateId` | string | Yes | 1 to 100 characters. Must name a registered template. |
| `parameters` | object | Yes | Flat map. Keys: 1 to 64 characters, letters, digits and `_`. Values: strings of at most 4,000 characters. At most 50 keys. A number, boolean, null, array or object value is refused. |

Recipient keys inside `parameters`:

| Channel | Key | Rule |
|---|---|---|
| `email` | `to` | Exactly 1 address in `local@domain` form, at most 254 characters. No display name and no list. |
| `teams` | `teamsDestination` | A Teams destination name: 1 to 64 characters, lowercase letters, digits and `-`. |

Example:

```http
POST /v1/notifications
Authorization: Bearer eyJ...
Idempotency-Key: 3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10
Content-Type: application/json

{
  "channel": "email",
  "templateId": "account-opened",
  "parameters": {
    "to": "jane@example.com",
    "customerName": "Jane",
    "accountNumber": "0012345678"
  }
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json

{ "notificationId": "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47" }
```

A 200 means accepted: queued or skipped. It never means delivered, and it carries no `Location` header. The 200 is the same for Queued and Skipped. The status tells delivered from not delivered, and never says why (the requester's rule). The caller reads it with `GET /v1/notifications/{notificationId}`, below.

Responses:

| Status | Code in `errors[].code` | When |
|---|---|---|
| 200 | — | Queued or Skipped. |
| 400 | `INVALID_REQUEST` | The body breaks a field rule above. |
| 400 | `TEMPLATE_NOT_FOUND` | `templateId` names no registered template. |
| 400 | `RECIPIENT_MISSING` | The channel's recipient key is absent or empty. |
| 400 | `RECIPIENT_INVALID` | The recipient value breaks its rule. |
| 400 | `PARAMETER_MISSING` | The template version holds a token with no parameter. The first missing name is given. |
| 400 | `MESSAGE_TOO_LARGE` | The rendered Teams card is above 28 KB, the Teams webhook limit. |
| 400 | — | `Idempotency-Key` is missing or breaks its rule. |
| 401 | — | No token, or the token is invalid. Empty body. |
| 403 | — | The token lacks `notifications.send`. Empty body. |
| 409 | — | The same key from the same caller is still in flight, or got a non-2xx answer under 30 seconds ago. A 409 never means accepted. |
| 413 | — | The body is above 64 KB. |
| 503 | `QUEUE_FULL` | The delivery list holds `Delivery:QueueCapacity` notifications (1,000 by default) for the whole service. `Retry-After: 30` is sent. |

Error bodies are problem details with `errors[]` and `traceId`, the shape DKNet.AspCore.Extensions builds for DKNet.Accounts.Api.

A repeated call with the same key from the same caller within 4 hours gets the first 200 replayed, with the same `notificationId`. Nothing is sent twice. Only 2xx responses are kept.

**Release note.** For 4 hours after the release that moved the answer from 202 to 200 (ADR-0012), an idempotent replay of a call accepted before the release answers the stored 202, because the idempotency records kept before it hold a 202. New calls answer 200. Callers accept both until then.

This needs 3 DKNet.AspCore.Idempotency settings that are not the package defaults (ADR-0008):

| Setting | Value | Package default | Why |
|---|---|---|---|
| `KeyScopeResolver` | Returns the caller id: the first of the `client_id`, `azp`, `appid` claims | Not set: user name id, then an HMAC of the `Authorization` header, then empty | Two callers never share a key, and a token refresh keeps the same scope |
| `ConflictHandling` | `CachedResult` | `ConflictResponse` (409) | A repeated call gets the first 200 replayed, not a 409 |
| `IdempotencyHeaderKey` | `Idempotency-Key` | `X-Idempotency-Key` | Same header name as DKNet.Accounts.Api |

After any non-2xx answer, the key stays reserved for up to 30 seconds from the first call. To retry, wait 30 seconds and send the same key again. If the first call was accepted, its 200 is then replayed; if not, the call runs afresh.

### `GET /v1/notifications/{notificationId}`

Reads the status of one notification the caller accepted. The route needs the same permission as `POST` (ADR-0007), and answers only for the caller's own notifications (ADR-0012).

Headers:

- `Authorization: Bearer <token>` — required.

`notificationId` is a GUID, the value `POST` answered.

```http
GET /v1/notifications/8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47
Authorization: Bearer eyJ...
```

```http
HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: no-store

{ "notificationId": "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47", "idempotencyKey": "3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10", "status": "pending" }
```

| `status` | Meaning | Final |
|---|---|---|
| `pending` | Queued, being delivered, or waiting for a retry. | No |
| `success` | The provider accepted the message. | Yes |
| `failed` | Delivery gave up, or the channel was unavailable. The reason is never shown. | Yes |

`idempotencyKey` is the `Idempotency-Key` header of the call that accepted the notification. A record is kept for 24 hours after its last write by default (`Notifications:Status:RetentionHours`, 1 to 168).

Responses:

| Status | Code in `errors[].code` | When |
|---|---|---|
| 200 | — | A record exists for this caller and this id. |
| 401 | — | No token, or the token is invalid. Empty body. |
| 403 | — | The token lacks `notifications.send`. Empty body. |
| 404 | `NOTIFICATION_NOT_FOUND` | The id is unknown, belongs to another caller, or has expired. The three cases look the same. |
| 404 | — | The id is not a GUID (the route does not match). |
| 429 | — | The existing rate limit. |
| 500 | — | Redis cannot be read. |

A status write is best effort and is not retried (05-quality). A `pending` that never changes means the final write failed or the queued message could not be read. The record then expires on its own.

### Evaluation order

Each check runs only when the one before it passed.

1. Authenticate the token (401), then check the scope or app role (403).
2. Check `Idempotency-Key` (400). Replay a kept 200, or answer 409 for a key still in flight.
3. Check the body against the field rules (413, then 400 `INVALID_REQUEST`).
4. Find the template (400 `TEMPLATE_NOT_FOUND`).
5. Resolve the channel. It ends **Skipped** when:
   - the channel is not supported by this release, or
   - the channel is not configured in this deployment (for email: `EmailChannelSettings` in 04-data), or
   - the template has no version for the channel.
6. Check the recipient key (400 `RECIPIENT_MISSING`, 400 `RECIPIENT_INVALID`).
7. Teams only: the destination name is not set in this deployment → **Skipped**.
8. Render the template version (400 `PARAMETER_MISSING`, 400 `MESSAGE_TOO_LARGE`).
9. Check that the delivery list has room (503 `QUEUE_FULL`), write the status `pending`, then publish the notification to the delivery queue.
10. Answer 200.

The idempotency check is an endpoint filter, so it runs after the request body is bound. A malformed or oversized body (step 3) can therefore be answered before step 2. No key is reserved in that case, so the order makes no difference to the caller.

A **Skipped** end (steps 5 and 7) writes the status `failed` and publishes nothing. A **Rejected** end (400 or 503) writes no status, because no id is ever returned for it.

`channel` is never bound to a fixed list at the API edge. A fixed list would turn an unknown channel into a 400, which breaks the skip rule.

## Published events

None — the service publishes no events. Outcomes are log entries (05-quality) and the status record the caller reads (04-data).

## Consumed APIs and events

The service consumes no events.

| Source | What | Why | What happens when it is down |
|---|---|---|---|
| Microsoft Entra ID | OpenID Connect metadata and signing keys | Validate caller tokens | Keys already loaded keep working. With no keys loaded, calls fail with 401. |
| Redis | Idempotency records, the delivery queue, notification status records | Detect repeated calls across replicas; carry waiting notifications between replicas and across restarts; let a caller read its status | `POST` and `GET` fail with 500 until Redis returns. The package does not catch store errors. Delivery waits: nothing can be taken from the queue. Notifications already queued stay in Redis. |
| SMTP provider | SMTP submission | Deliver email when `Sender` is `Smtp` | Transient failure: retried. After attempt 3 the notification ends Failed and is logged. |
| Microsoft Entra ID token endpoint | Client credentials token for `https://graph.microsoft.com/.default` | Sign in to Graph when `Sender` is `Graph` (ADR-0010) | 408, 429, 5xx, a timeout and a lost connection are transient and retried; a 429 waits for `Retry-After`, at most 60 seconds. Other 4xx, such as `invalid_client`, end Failed at once. |
| Microsoft Graph | `POST /users/{Mailbox}/sendMail` | Deliver email when `Sender` is `Graph` (ADR-0009) | 408, 429, 5xx and timeouts are retried. Other 4xx, such as 403 for a mailbox outside the app's scope, end Failed. |
| Teams Workflows webhook | HTTPS POST | Deliver the Teams card | 429, 5xx and timeouts are retried. Other 4xx, such as a deleted workflow, end Failed. |
| DKNet.Svc.Transformation | Token replacement | Render template versions | In process; not a runtime dependency. |
| DKNet.AspCore.Idempotency and its Redis store | Idempotency filter and store | Replay the first 200 to repeated calls | In process; depends on Redis above. |
| DKNet.AspCore.Extensions | Problem-details error bodies, endpoint scope declarations | One error shape with DKNet.Accounts.Api | In process; not a runtime dependency. |

### Outbound call — Microsoft Graph `sendMail`

The Graph sender makes one call per delivery attempt. The token comes first; the service reuses it until it is close to expiry.

```http
POST https://graph.microsoft.com/v1.0/users/notify@contoso.com/sendMail
Authorization: Bearer eyJ...
Content-Type: application/json

{
  "message": {
    "subject": "Your account is open",
    "body": { "contentType": "HTML", "content": "<p>Dear Jane, ...</p>" },
    "toRecipients": [ { "emailAddress": { "address": "jane@example.com" } } ]
  }
}
```

```http
HTTP/1.1 202 Accepted
```

- The path names the `Mailbox` setting. The caller never names the mailbox.
- `toRecipients` holds exactly the one `to` address. No CC, BCC, attachment or `from` value is sent.
- `saveToSentItems` is left out, so Graph saves the mail to Sent Items, its default (ADR-0009).
- A 202 means Graph accepted the mail. It does not mean the mail reached the recipient.

How each answer counts for the retry rule (ADR-0013, ADR-0009):

| Step | Answer | Kind | What the consumer does |
|---|---|---|---|
| Token request | Token issued | — | Goes on to `sendMail` |
| Token request | HTTP 429 | Transient | Waits for `Retry-After`, at most 60 seconds; without the header, waits as below |
| Token request | HTTP 408, 5xx, timeout, lost connection | Transient | Waits 5 seconds, then 30 seconds; at most 3 attempts |
| Token request | Any other HTTP 4xx, such as `invalid_client` or a federated credential that does not match | Permanent | Ends Failed at once |
| `sendMail` | 202 | — | Ends Delivered |
| `sendMail` | 429 | Transient | Waits for `Retry-After`, at most 60 seconds; without the header, waits as below |
| `sendMail` | 408, 5xx, timeout, lost connection | Transient | Waits 5 seconds, then 30 seconds; at most 3 attempts |
| `sendMail` | 400, 401, 403, 404, any other 4xx | Permanent | Ends Failed at once. A 403 most often means the mailbox is outside the app's `Mail.Send` scope |

No library retries inside an attempt. The `Azure.Identity` credential is built with its retry turned off (`Retry.MaxRetries` = 0); its default would retry 3 times. The consumer's rule is the only retry, so `AttemptCount` stays at most 3. A wait is not held in memory: the consumer publishes the message back to the queue with a `NotBefore` time (ADR-0013).

## Dependencies

| Depends on | Kind | Direction | Notes |
|---|---|---|---|
| DKNet.Svc.Transformation | NuGet library | service → library | ADR-0004 |
| DKNet.AspCore.Idempotency, DKNet.AspCore.Idempotency.RedisStore | NuGet library | service → library | Same packages DKNet.Accounts.Api uses |
| DKNet.AspCore.Extensions | NuGet library | service → library | Error responses, endpoint scopes |
| DKNet.SlimBus.Extensions, SlimMessageBus.Host.Memory | NuGet library | service → library | In-process mediator from endpoint to handler. ADR-0011 |
| SlimMessageBus.Host.Redis, SlimMessageBus.Host.Serialization.SystemTextJson | NuGet library | service → library | The delivery queue on a Redis list. ADR-0013 |
| MailKit | NuGet library (third party) | service → library | New to the DKNet repos. ADR-0005 |
| Azure.Identity | NuGet library (third party) | service → library | Graph token, both credential modes. DKNet.Accounts.Api pins it too. ADR-0010 |
| DKNet.Templates | Solution template | one-time scaffold | Not referenced after slice 1 |
| Microsoft Entra ID | External service | service → Entra ID | Token validation; with the Graph sender, also the mail-sender app's token request |
| Redis | External store | service → Redis | ADR-0002, ADR-0012, ADR-0013 |
| SMTP provider | External service | service → provider | ADR-0005 |
| Microsoft Graph and the sending mailbox | External service | service → Graph | ADR-0009, ADR-0010 |
| Microsoft Teams Workflows | External service | service → webhook | ADR-0006 |

Every arrow points from this service to a library or an external system. No library points back. No DKNet repo depends on this service at build time, so there is no cycle. DKNet.SlimBus.Extensions supplies only the command and handler contracts; its EF Core helpers are not used (ADR-0011).

## Main flows

### Flow 1 — Send an email, happy path

1. A caller gets a token for `notifications.send` from Entra ID.
2. The caller posts `channel: email`, a template id and parameters holding `to`.
3. The service validates the token and the idempotency key, and reserves the key in Redis.
4. The service finds the template and its email version, and checks `to`.
5. The service HTML-encodes each parameter value and fills the body and subject tokens.
6. The service checks the delivery list has room, writes the status `pending`, publishes the notification to the queue, keeps the 200 under the key, and answers 200 with the `notificationId`.
7. The delivery consumer takes the notification from the queue and submits it to the SMTP provider.
8. The provider accepts it. The notification ends Delivered, the status becomes `success`, and one log entry records it.

Failure paths:

- Step 4 finds no `to`, or a bad address: 400. Nothing is queued.
- Step 5 finds a token with no parameter: 400 `PARAMETER_MISSING`. Nothing is queued.
- Step 6 finds the queue full: 503 `QUEUE_FULL`. Nothing is written. The caller retries after 30 seconds with the same key.
- Step 7 gets a transient failure: the consumer publishes the message back with a wait of 5 seconds, then 30 seconds, and a replica takes it again. After attempt 3 the notification ends Failed and the status becomes `failed`.
- Step 7 gets a permanent failure: the notification ends Failed at once.
- A replica stops during step 7: the message goes back to the queue without counting the attempt. A replica that stops while a message waits for its time loses nothing, because the message is in Redis.
- The caller reads `pending`, then `success` or `failed`, with `GET /v1/notifications/{notificationId}` at any point after step 6.

This flow is the SMTP sender. With `Sender` set to `Graph`, steps 7 and 8 run as in Flow 4.

![Sequence of one email notification from the caller through token check, idempotency reservation, rendering, the pending status and publish to the Redis delivery queue and 200, then the delivery consumer's SMTP submission with its retry path back through the queue, and the final status.](diagrams/send-email.svg)

### Flow 2 — Channel unavailable

1. A caller posts a channel this release does not support, such as `whatsapp`.
2. Steps 1 to 4 of the evaluation order pass.
3. Step 5 finds no channel sender. The notification ends Skipped.
4. The service writes one warning log entry: `notificationId`, template id, channel and reason. It logs no parameter and no recipient.
5. The service writes the status `failed`, publishes nothing, and answers 200 with the `notificationId`.

The same flow runs when the channel is supported but not configured, when the template has no version for the channel, and when the Teams destination name is not set in this deployment.

![Sequence of a call for an unsupported channel: checks 1 to 4 pass, the channel check fails, a warning is logged without personal data, and the caller gets 200.](diagrams/channel-skipped.svg)

### Flow 3 — Post to Microsoft Teams

1. A caller posts `channel: teams`, a template id and parameters holding `teamsDestination`.
2. The service finds the template's Teams version and the destination's webhook URL in the settings.
3. The service fills the Markdown body and the optional title. The values go in unchanged.
4. The service builds the Adaptive Card message: one title text block and one Markdown text block. A JSON serializer builds it, so values cannot break the JSON.
5. The service checks the card is at most 28 KB, queues it (with the status `pending`) and answers 200.
6. The delivery consumer posts the card to the webhook URL. A 2xx answer ends Delivered and the status becomes `success`.

Failure paths:

- Step 2 finds no destination with that name: Skipped, as in Flow 2.
- Step 6 gets 429: the message goes back to the queue to wait for the `Retry-After` value, at most 60 seconds, then retries within the 3 attempts.
- Step 6 gets 404 or another non-retryable 4xx: the notification ends Failed.

![Sequence of a Teams notification: the destination name resolves to a webhook URL, the card is queued and 200 returned, then the delivery consumer posts it, puts it back to wait out a 429 and delivers on attempt 2.](diagrams/send-teams.svg)

### Flow 4 — Send an email through Microsoft Graph

The deployment sets `Sender` to `Graph`. Steps 1 to 6 are those of Flow 1, and the caller sees the same 200.

1. The delivery consumer takes the notification from the queue.
2. The consumer asks Entra ID for a token for `https://graph.microsoft.com/.default`, as the mail-sender app. It skips this step while the token it holds is still valid.
3. Entra ID checks the app's credential: the Kubernetes service account token (`WorkloadIdentity`) or the client secret. It returns an access token.
4. The consumer posts the message to `sendMail` on the sending mailbox.
5. Graph answers 202. The notification ends Delivered, the status becomes `success`, and one log entry records it. Graph saves a copy in Sent Items.

Failure paths:

- Graph selected but a required setting missing: the host logs `EmailSenderNotConfigured` at start-up. Every email call ends Skipped at step 5 of the evaluation order, as in Flow 2.
- Step 2 gets 408, 429, 5xx or a timeout: transient, retried within the 3 attempts. A 429 waits for `Retry-After`, at most 60 seconds.
- Step 2 gets any other HTTP 4xx: permanent. The notification ends Failed.
- Step 4 gets 429: the message goes back to the queue to wait for `Retry-After`, at most 60 seconds, then retries within the 3 attempts.
- Step 4 gets 403: the mailbox is outside the app's scope, or the scope is still being applied. The notification ends Failed.
- Step 4 times out after Graph already took the mail: the retry can send it twice (ADR-0013).

![Sequence of one email through Microsoft Graph: the API accepts and queues it as in Flow 1, then the delivery consumer gets an Entra ID token as the mail-sender app, posts to sendMail on the one mailbox, puts the message back to wait out a 429 and gets 202 on attempt 2.](diagrams/send-email-graph.svg)
