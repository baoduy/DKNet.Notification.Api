# 03 — Integration

## Context map

| Neighbour | Direction | How they talk |
|---|---|---|
| Backend callers (DKNet.Accounts.Api is a likely first) | Caller → this service | HTTPS REST, Entra ID bearer token |
| Microsoft Entra ID | This service → Entra ID | OpenID Connect metadata and signing keys, read to validate tokens |
| Redis | This service → Redis | Redis protocol, for idempotency records only |
| SMTP provider | This service → provider | SMTP with STARTTLS or TLS, authenticated |
| Microsoft Teams Workflows webhook | This service → webhook | HTTPS POST of an Adaptive Card message |
| DKNet packages (DKNet repo) | This service → packages | NuGet package references, in process |
| DKNet.Templates | One-time, at scaffold | `dotnet new dknet-minimal`; no runtime link |

This service calls no other DKNet service. No DKNet service is called by it. No library depends on it.

![DKNet Notification sits between backend callers and two delivery targets, the SMTP provider and Teams Workflows webhooks, and depends on Entra ID for tokens, Redis for idempotency records, and DKNet packages at build time.](diagrams/context-map.svg)

## Exposed API

| Verb | Path | Purpose | Auth |
|---|---|---|---|
| POST | `/v1/notifications` | Accept one notification for one channel. | Bearer token with scope or app role `notifications.send` (ADR-0007) |
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
HTTP/1.1 202 Accepted
Content-Type: application/json

{ "notificationId": "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47" }
```

A 202 means accepted: queued or skipped. It never means delivered. Queued and Skipped get the same response, so a caller cannot tell them apart (the requester's rule).

Responses:

| Status | Code in `errors[].code` | When |
|---|---|---|
| 202 | — | Queued or Skipped. |
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
| 503 | `QUEUE_FULL` | The delivery queue holds 1,000 notifications. `Retry-After: 30` is sent. |

Error bodies are problem details with `errors[]` and `traceId`, the shape DKNet.AspCore.Extensions builds for DKNet.Accounts.Api.

A repeated call with the same key from the same caller within 4 hours gets the first 202 replayed, with the same `notificationId`. Nothing is sent twice. Only 2xx responses are kept.

This needs 3 DKNet.AspCore.Idempotency settings that are not the package defaults (ADR-0008):

| Setting | Value | Package default | Why |
|---|---|---|---|
| `KeyScopeResolver` | Returns the caller id: the first of the `client_id`, `azp`, `appid` claims | Not set: user name id, then an HMAC of the `Authorization` header, then empty | Two callers never share a key, and a token refresh keeps the same scope |
| `ConflictHandling` | `CachedResult` | `ConflictResponse` (409) | A repeated call gets the first 202 replayed, not a 409 |
| `IdempotencyHeaderKey` | `Idempotency-Key` | `X-Idempotency-Key` | Same header name as DKNet.Accounts.Api |

After any non-2xx answer, the key stays reserved for up to 30 seconds from the first call. To retry, wait 30 seconds and send the same key again. If the first call was accepted, its 202 is then replayed; if not, the call runs afresh.

### Evaluation order

Each check runs only when the one before it passed.

1. Authenticate the token (401), then check the scope or app role (403).
2. Check `Idempotency-Key` (400). Replay a kept 202, or answer 409 for a key still in flight.
3. Check the body against the field rules (413, then 400 `INVALID_REQUEST`).
4. Find the template (400 `TEMPLATE_NOT_FOUND`).
5. Resolve the channel. It ends **Skipped** when:
   - the channel is not supported by this release, or
   - the channel is not configured in this deployment, or
   - the template has no version for the channel.
6. Check the recipient key (400 `RECIPIENT_MISSING`, 400 `RECIPIENT_INVALID`).
7. Teams only: the destination name is not set in this deployment → **Skipped**.
8. Render the template version (400 `PARAMETER_MISSING`, 400 `MESSAGE_TOO_LARGE`).
9. Put the notification in the delivery queue (503 `QUEUE_FULL`).
10. Answer 202.

The idempotency check is an endpoint filter, so it runs after the request body is bound. A malformed or oversized body (step 3) can therefore be answered before step 2. No key is reserved in that case, so the order makes no difference to the caller.

`channel` is never bound to a fixed list at the API edge. A fixed list would turn an unknown channel into a 400, which breaks the skip rule.

## Published events

None — the service publishes no events. Outcomes are log entries only (05-quality).

## Consumed APIs and events

The service consumes no events.

| Source | What | Why | What happens when it is down |
|---|---|---|---|
| Microsoft Entra ID | OpenID Connect metadata and signing keys | Validate caller tokens | Keys already loaded keep working. With no keys loaded, calls fail with 401. |
| Redis | Idempotency records | Detect repeated calls across replicas | Calls fail with 500 until Redis returns. The package does not catch store errors. |
| SMTP provider | SMTP submission | Deliver email | Transient failure: retried. After attempt 3 the notification ends Failed and is logged. |
| Teams Workflows webhook | HTTPS POST | Deliver the Teams card | 429, 5xx and timeouts are retried. Other 4xx, such as a deleted workflow, end Failed. |
| DKNet.Svc.Transformation | Token replacement | Render template versions | In process; not a runtime dependency. |
| DKNet.AspCore.Idempotency and its Redis store | Idempotency filter and store | Replay the first 202 to repeated calls | In process; depends on Redis above. |
| DKNet.AspCore.Extensions | Problem-details error bodies, endpoint scope declarations | One error shape with DKNet.Accounts.Api | In process; not a runtime dependency. |

## Dependencies

| Depends on | Kind | Direction | Notes |
|---|---|---|---|
| DKNet.Svc.Transformation | NuGet library | service → library | ADR-0004 |
| DKNet.AspCore.Idempotency, DKNet.AspCore.Idempotency.RedisStore | NuGet library | service → library | Same packages DKNet.Accounts.Api uses |
| DKNet.AspCore.Extensions | NuGet library | service → library | Error responses, endpoint scopes |
| MailKit | NuGet library (third party) | service → library | New to the DKNet repos. ADR-0005 |
| DKNet.Templates | Solution template | one-time scaffold | Not referenced after slice 1 |
| Microsoft Entra ID | External service | service → Entra ID | Token validation only |
| Redis | External store | service → Redis | ADR-0002 |
| SMTP provider | External service | service → provider | ADR-0005 |
| Microsoft Teams Workflows | External service | service → webhook | ADR-0006 |

Every arrow points from this service to a library or an external system. No library points back. No DKNet repo depends on this service at build time, so there is no cycle. DKNet.SlimBus.Extensions is not used (ADR-0003).

## Main flows

### Flow 1 — Send an email, happy path

1. A caller gets a token for `notifications.send` from Entra ID.
2. The caller posts `channel: email`, a template id and parameters holding `to`.
3. The service validates the token and the idempotency key, and reserves the key in Redis.
4. The service finds the template and its email version, and checks `to`.
5. The service HTML-encodes each parameter value and fills the body and subject tokens.
6. The service puts the notification in the queue, keeps the 202 under the key, and answers 202 with the `notificationId`.
7. The delivery worker takes the notification and submits it to the SMTP provider.
8. The provider accepts it. The notification ends Delivered, and one log entry records it.

Failure paths:

- Step 4 finds no `to`, or a bad address: 400. Nothing is queued.
- Step 5 finds a token with no parameter: 400 `PARAMETER_MISSING`. Nothing is queued.
- Step 6 finds the queue full: 503 `QUEUE_FULL`. The caller retries after 30 seconds with the same key.
- Step 7 gets a transient failure: the worker waits 5 seconds, then 30 seconds, and tries again. After attempt 3 the notification ends Failed.
- Step 7 gets a permanent failure: the notification ends Failed at once.

![Sequence of one email notification from the caller through token check, idempotency reservation, rendering, queueing and 202, then the worker's SMTP submission with its retry path.](diagrams/send-email.svg)

### Flow 2 — Channel unavailable

1. A caller posts a channel this release does not support, such as `whatsapp`.
2. Steps 1 to 4 of the evaluation order pass.
3. Step 5 finds no channel sender. The notification ends Skipped.
4. The service writes one warning log entry: `notificationId`, template id, channel and reason. It logs no parameter and no recipient.
5. The service answers 202 with the `notificationId`.

The same flow runs when the channel is supported but not configured, when the template has no version for the channel, and when the Teams destination name is not set in this deployment.

![Sequence of a call for an unsupported channel: checks 1 to 4 pass, the channel check fails, a warning is logged without personal data, and the caller gets 202.](diagrams/channel-skipped.svg)

### Flow 3 — Post to Microsoft Teams

1. A caller posts `channel: teams`, a template id and parameters holding `teamsDestination`.
2. The service finds the template's Teams version and the destination's webhook URL in the settings.
3. The service fills the Markdown body and the optional title. The values go in unchanged.
4. The service builds the Adaptive Card message: one title text block and one Markdown text block. A JSON serializer builds it, so values cannot break the JSON.
5. The service checks the card is at most 28 KB, queues it and answers 202.
6. The delivery worker posts the card to the webhook URL. A 2xx answer ends Delivered.

Failure paths:

- Step 2 finds no destination with that name: Skipped, as in Flow 2.
- Step 6 gets 429: the worker waits for the `Retry-After` value, at most 60 seconds, then retries within the 3 attempts.
- Step 6 gets 404 or another non-retryable 4xx: the notification ends Failed.

![Sequence of a Teams notification: the destination name resolves to a webhook URL, the card is queued and 202 returned, then the worker posts it, waits out a 429 and delivers on attempt 2.](diagrams/send-teams.svg)
