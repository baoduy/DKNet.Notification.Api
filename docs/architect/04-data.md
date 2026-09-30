# 04 — Data

## Ownership

The service owns:

- The template catalogue: registrations and template files. They ship inside the release.
- The channel settings and Teams destinations of its deployment.
- Idempotency records for its own endpoint.
- In-memory notifications, from acceptance to their end state.

The service never writes:

- Recipients' contact data. Callers own it and pass it per call.
- Any account, customer or ledger data. DKNet.Accounts.Api owns it.
- Identities and tokens. Microsoft Entra ID owns them.
- Delivered mail and Teams posts. The SMTP provider and Teams own them after hand-off.

## Storage

| Data | Store | Why |
|---|---|---|
| Template catalogue | `appsettings.json` registrations plus files in the API project's `Templates` folder, copied into the image | The requester's rule: templates change only by release (ADR-0004) |
| Channel settings and Teams destinations | Configuration: `appsettings.json` for non-secret values; environment variables or Azure App Configuration for secrets | One set per deployment. Same configuration order as DKNet.Accounts.Api |
| Idempotency records | Redis | Shared by all replicas; expires on its own (ADR-0002) |
| Notifications | Process memory only | No status tracking in version 1 (ADR-0002, ADR-0003) |

There is no relational database. The scaffold's PostgreSQL resource, EF Core context and database health check are removed in slice 1 (ADR-0002).

The in-memory idempotency store is allowed only for local runs and tests. The package itself warns that it is not for production.

## Entities

### Notification (in memory, not stored)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| NotificationId | GUID | — | Yes | Unique | New GUID on acceptance | Returned to the caller; in every log entry |
| TemplateId | string | 1–100 | Yes | — | — | Reference to NotificationTemplate |
| Channel | string | 1–50 | Yes | — | — | Lower-cased caller value |
| Parameters | map of string to string | ≤ 50 keys; key 1–64; value ≤ 4,000 | Yes | — | — | **Personal data.** Never logged |
| Recipient | EmailRecipient or TeamsRecipient | address ≤ 254; destination 1–64 | Yes, when queued | — | — | **Personal data** for email. Never logged |
| RenderedSubject | string | ≤ 998 | Email only | — | — | Filled subject; CR and LF replaced by spaces |
| RenderedTitle | string | ≤ 500 | No | — | Empty | Teams card title, when the version has one |
| RenderedBody | string | Teams card ≤ 28 KB | Yes, when queued | — | — | **Personal data.** Never logged |
| BodyFormat | enum | — | Yes | — | — | `Html` (email), `Markdown` (Teams) |
| CallerId | string | ≤ 256 | Yes | — | — | First of the `client_id`, `azp`, `appid` claims |
| Status | enum | — | Yes | — | `Received` | `Received`, `Rejected`, `Skipped`, `Queued`, `Delivering`, `RetryWaiting`, `Delivered`, `Failed` |
| AttemptCount | integer | 0–3 | Yes | — | 0 | Delivery attempts made |
| AcceptedAt | timestamp (UTC) | milliseconds | Yes | — | Now | For log timing |

### NotificationTemplate (configuration, read-only)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| TemplateId | string | 1–100 | Yes | Unique | — | Lowercase letters, digits and `-` |
| Description | string | ≤ 200 | No | — | Empty | For template authors only |
| Versions | list of TemplateVersion | 1–2 items | Yes | One per channel | — | See below |

### TemplateVersion (configuration, read-only)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Channel | string | 1–50 | Yes | Unique within its template | — | `email` or `teams` in version 1 |
| File | string | ≤ 200 | Yes | — | — | Relative path inside the `Templates` folder |
| Format | enum | — | Yes | — | — | `Html` for email, `Markdown` for Teams |
| Subject | string | 1–500 | Email only | — | — | May hold `{{name}}` tokens |
| Title | string | ≤ 200 | No | — | Empty | Teams only. May hold `{{name}}` tokens |

### EmailChannelSettings (configuration, one per deployment)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Enabled | boolean | — | Yes | — | `false` | `false` means not configured: every email call ends Skipped |
| Host | string | ≤ 255 | When enabled | — | — | SMTP host |
| Port | integer | 1–65535 | When enabled | — | 587 | |
| Security | enum | — | When enabled | — | `StartTls` | `StartTls` or `Tls`. No plain-text option |
| UserName | string | ≤ 256 | No | — | Empty | |
| Password | string | ≤ 512 | No | — | Empty | **Secret.** Environment variable or Azure App Configuration only |
| FromAddress | string | ≤ 254 | When enabled | — | — | Sender address for every email |
| FromName | string | ≤ 100 | No | — | `DKNet Notification` | Sender display name |
| TimeoutSeconds | integer | 1–120 | Yes | — | 30 | Per delivery attempt |

### TeamsChannelSettings (configuration, one per deployment)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Enabled | boolean | — | Yes | — | `false` | `false` means not configured: every Teams call ends Skipped |
| Destinations | map of name to TeamsDestination | 0–100 items | No | Name unique | Empty | |
| TimeoutSeconds | integer | 1–120 | Yes | — | 30 | Per delivery attempt |

### TeamsDestination (configuration)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Name | string | 1–64 | Yes | Unique | — | Lowercase letters, digits and `-`; the value callers send |
| WebhookUrl | string (HTTPS URL) | ≤ 2,048 | Yes | — | — | **Secret.** Anyone with it can post. Environment variable or Azure App Configuration only |

### DeliverySettings (configuration)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| QueueCapacity | integer | 1–100,000 | Yes | — | 1,000 | Full queue answers 503 |
| MaxAttempts | integer | 1–3 | Yes | — | 3 | |
| RetryDelaysSeconds | list of integer | 2 items, each 1–300 | Yes | — | `[5, 30]` | Wait before attempt 2 and attempt 3 |

### IdempotencyRecord (Redis, shape owned by DKNet.AspCore.Idempotency)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Key | string | — | Yes | Unique | — | Caller id (from the `KeyScopeResolver` setting, ADR-0008) + route + method + `Idempotency-Key` |
| StatusCode | integer | — | Yes | — | 102 while in flight | 202 once kept |
| Body | string | — | No | — | — | The 202 body: `notificationId` only |
| ContentType | string | — | Yes | — | — | `application/json` |
| CreatedAt | timestamp (UTC) | — | Yes | — | Now | |
| ExpiresAt | timestamp (UTC) | — | Yes | — | +30 seconds in flight; +4 hours kept | Redis expiry follows it |

## Retention

| Record | Lives for | Deleted by |
|---|---|---|
| Notification | Until its end state; lost on process stop | The process |
| Idempotency record in flight | 30 seconds | Redis expiry |
| Idempotency record kept | 4 hours | Redis expiry |
| Template catalogue and settings | Until the next release or configuration change | The release or the operator |
| Log entries | The log platform's retention | The log platform. No personal data is in them |
