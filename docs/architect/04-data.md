# 04 — Data

## Ownership

The service owns:

- The template catalogue: registrations and template files. They ship inside the release.
- The channel settings and Teams destinations of its deployment.
- Idempotency records for its own endpoint.
- The delivery queue: one message in a Redis list for each notification that waits for an attempt (ADR-0013).
- The status record of each accepted notification, for the caller that sent it (ADR-0012).
- In-memory notifications, while a delivery attempt runs.

The service never writes:

- Recipients' contact data. Callers own it and pass it per call.
- Any account, customer or ledger data. DKNet.Accounts.Api owns it.
- Identities and tokens. Microsoft Entra ID owns them.
- Delivered mail and Teams posts. The SMTP provider, Microsoft 365 and Teams own them after hand-off.
- The copy of each Graph email in the sending mailbox's Sent Items. The Microsoft 365 tenant owns it, under its own retention (ADR-0009).
- The mail-sender app registration, its credential and its `Mail.Send` grant. The tenant's Entra ID and Exchange Online administrators own them (ADR-0010).

## Storage

| Data | Store | Why |
|---|---|---|
| Template catalogue | `appsettings.json` registrations plus files in the API project's `Templates` folder, copied into the image | The requester's rule: templates change only by release (ADR-0004) |
| Channel settings, email sender settings and Teams destinations | Configuration: `appsettings.json` for non-secret values; environment variables or Azure App Configuration for secrets | One set per deployment. Same configuration order as DKNet.Accounts.Api |
| Idempotency records | Redis | Shared by all replicas; expires on its own (ADR-0002) |
| Notifications waiting for delivery | Redis list `notification-delivery` (process memory in local runs and tests) | Waiting notifications and retry waits survive a restart or a deploy (ADR-0013) |
| Notification status | Redis key `status:{callerId}:{notificationId}` through `IDistributedCache` (process memory in local runs and tests) | A caller reads the status of its own notification from any replica (ADR-0012) |
| Notification during an attempt | Process memory | Rebuilt from the queued message for each attempt |

There is no relational database. The scaffold's PostgreSQL resource, EF Core context and database health check are removed in slice 1 (ADR-0002).

The in-memory idempotency store, the memory delivery bus and the in-memory status store are allowed only for local runs and tests. The package itself warns that the idempotency store is not for production. Outside Development and Testing the service refuses to start without `ConnectionStrings:Redis`.

## Entities

### Notification (in memory during an attempt; rebuilt from the queued message)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| NotificationId | GUID | — | Yes | Unique | New GUID on acceptance | Returned to the caller; in every log entry |
| TemplateId | string | 1–100 | Yes | — | — | Reference to NotificationTemplate |
| Channel | string | 1–50 | Yes | — | — | Lower-cased caller value |
| Parameters | map of string to string | ≤ 50 keys; key 1–64; value ≤ 4,000 | Yes | — | — | **Personal data.** Never logged. Empty on a resumed notification: the queued message holds the rendered message, not the parameters |
| Recipient | EmailRecipient or TeamsRecipient | address ≤ 254; destination 1–64 | Yes, when queued | — | — | **Personal data** for email. Never logged |
| RenderedSubject | string | ≤ 998 | Email only | — | — | Filled subject; CR and LF replaced by spaces |
| RenderedTitle | string | ≤ 500 | No | — | Empty | Teams card title, when the version has one |
| RenderedBody | string | Teams card ≤ 28 KB | Yes, when queued | — | — | **Personal data.** Never logged |
| BodyFormat | enum | — | Yes | — | — | `Html` (email), `Markdown` (Teams) |
| CallerId | string | ≤ 256 | Yes | — | — | First of the `client_id`, `azp`, `appid` claims |
| Status | enum | — | Yes | — | `Received` | `Received`, `Rejected`, `Skipped`, `Queued`, `Delivering`, `RetryWaiting`, `Delivered`, `Failed` |
| AttemptCount | integer | 0–3 | Yes | — | 0 | Delivery attempts made |
| AcceptedAt | timestamp (UTC) | milliseconds | Yes | — | Now | For log timing |

### DeliverNotification (Redis list `notification-delivery`, one message per waiting notification)

The message is JSON, written by the System.Text.Json serializer. Local runs and tests keep it in memory as an object (ADR-0013).

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| SchemaVersion | integer | — | Yes | — | 1 | Fields are only ever added, as optional (ADR-0013) |
| NotificationId | GUID | — | Yes | — | — | The id returned to the caller; in every log entry |
| TemplateId | string | 1–100 | Yes | — | — | |
| Channel | string | 1–50 | Yes | — | — | Lower-cased caller value |
| CallerId | string | ≤ 256 | Yes | — | — | Scopes the status record |
| IdempotencyKey | string | 1–255 | No | — | — | The `Idempotency-Key` header of the accepting call. Copied into the status record |
| AcceptedAt | timestamp (UTC) | milliseconds | Yes | — | — | For the delivery duration |
| TraceId | string | — | Yes | — | — | The accepting call's trace id. Delivery links to it |
| EmailAddress | string | ≤ 254 | Email only | — | — | **Personal data.** Never logged |
| TeamsDestination | string | 1–64 | Teams only | — | — | The destination name, never the webhook URL |
| Subject | string | ≤ 998 | Yes | — | Empty | Email subject, or the Teams card title |
| Body | string | Teams card ≤ 28 KB | Yes | — | — | **Personal data.** Never logged |
| Format | enum | — | Yes | — | — | `Html` (email), `Markdown` (Teams) |
| AttemptsMade | integer | 0–2 | Yes | — | 0 | Delivery attempts already made |
| NotBefore | timestamp (UTC) | milliseconds | Yes | — | Now | The next attempt starts no sooner |

A message holds the rendered message and the one recipient, not the caller's parameters. It is not encrypted. Redis access control and TLS protect it, as they protect idempotency records. The message stays in Redis until a consumer takes it, so the personal data in it stays there until the notification is delivered or fails.

### NotificationStatusRecord (Redis key, through `IDistributedCache`)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Key | string | — | Yes | Unique | — | `status:{callerId}:{notificationId}`, under the cache's instance name. One record per caller and notification |
| NotificationId | GUID | — | Yes | — | — | |
| IdempotencyKey | string | 1–255 | No | — | — | The `Idempotency-Key` of the accepting call |
| Status | enum | — | Yes | — | `Pending` | `Pending`, `Success` or `Failed`. The API shows them as `pending`, `success`, `failed`; Skipped is `Failed` |
| ExpiresAt | timestamp (UTC) | — | Yes | — | +24 hours | Set on each write, so it counts from the last write. `Notifications:Status:RetentionHours`, 1–168. Redis expiry follows it |

The record holds no personal data.

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
| Subject | string | 1–500 | Email only | — | — | The HTML `<title>` of the file; it stays in the sent body. May hold `{{name}}` tokens |
| Title | string | ≤ 200 | No | — | Empty | Teams only. Read from the file's front matter (`title:`). May hold `{{name}}` tokens |

### EmailChannelSettings (configuration, one per deployment)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Enabled | boolean | — | Yes | — | `false` | `false` means not configured: every email call ends Skipped |
| Sender | enum | — | Yes | — | `Smtp` | `Smtp` or `Graph`. Picks the one email sender of this deployment (ADR-0009). Any other value means not configured |
| TimeoutSeconds | integer | 1–120 | Yes | — | 30 | Per delivery attempt, for either sender. With Graph it covers the token request and the send |

Email is configured when `Enabled` is `true`, `Sender` is `Smtp` or `Graph`, and every required field of that sender's settings is set. Otherwise every email call ends Skipped with reason `ChannelNotConfigured`, and the host still starts. When `Enabled` is `true` and email is still not configured, the host logs `EmailSenderNotConfigured` once (05-quality). It names the missing settings, or `Sender` when its value is unknown, and never a setting's value. With `Enabled` = `false` no start-up warning is logged, as in revision 1.

### SmtpSenderSettings (configuration, read only when `Sender` is `Smtp`)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Host | string | ≤ 255 | Yes | — | — | SMTP host |
| Port | integer | 1–65535 | Yes | — | 587 | |
| Security | enum | — | Yes | — | `StartTls` | `StartTls` or `Tls`. No plain-text option |
| UserName | string | ≤ 256 | No | — | Empty | |
| Password | string | ≤ 512 | No | — | Empty | **Secret.** Environment variable or Azure App Configuration only |
| FromAddress | string | ≤ 254 | Yes | — | — | Sender address for every email |
| FromName | string | ≤ 100 | No | — | `DKNet Notification` | Sender display name |

### GraphSenderSettings (configuration, read only when `Sender` is `Graph`)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| TenantId | GUID | — | Yes | — | — | Directory (tenant) id of the Microsoft 365 tenant |
| ClientId | GUID | — | Yes | — | — | Application (client) id of the mail-sender app registration. Never the API's own registration (ADR-0010) |
| Credential | enum | — | Yes | — | `WorkloadIdentity` | `WorkloadIdentity` or `ClientSecret` (ADR-0010) |
| ClientSecret | string | ≤ 512 | When `Credential` is `ClientSecret` | — | Empty | **Secret.** Environment variable or Azure App Configuration only; user secrets for local runs. Ignored with `WorkloadIdentity` |
| Mailbox | string | ≤ 254 | Yes | — | — | User principal name of the one sending mailbox, in `local@domain` form. Every Graph email is sent from it. The sender's display name is the mailbox's own |

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
| QueueCapacity | integer | 1–100,000 | Yes | — | 1,000 | For the whole service, not one replica. The API counts the Redis list before it publishes; a full list answers 503. The memory fallback has no limit |
| MaxAttempts | integer | 1–3 | Yes | — | 3 | |
| RetryDelaysSeconds | list of integer | 2 items, each 1–300 | Yes | — | `[5, 30]` | Wait before attempt 2 and attempt 3 |

### NotificationStatusSettings (configuration)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| RetentionHours | integer | 1–168 | Yes | — | 24 | `Notifications:Status:RetentionHours`. Hours a status record is kept after its last write. A bad value stops the start-up |

### IdempotencyRecord (Redis, shape owned by DKNet.AspCore.Idempotency)

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| Key | string | — | Yes | Unique | — | Caller id (from the `KeyScopeResolver` setting, ADR-0008) + route + method + `Idempotency-Key` |
| StatusCode | integer | — | Yes | — | 102 while in flight | 200 once kept. A record kept before the release of ADR-0012 holds 202 and replays it until it expires |
| Body | string | — | No | — | — | The 200 body: `notificationId` only |
| ContentType | string | — | Yes | — | — | `application/json` |
| CreatedAt | timestamp (UTC) | — | Yes | — | Now | |
| ExpiresAt | timestamp (UTC) | — | Yes | — | +30 seconds in flight; +4 hours kept | Redis expiry follows it |

## Retention

| Record | Lives for | Deleted by |
|---|---|---|
| Notification in memory | While one delivery attempt runs | The process |
| Queued message | Until a consumer takes it for an attempt. A retry publishes a new message. Survives a restart or a deploy; lost with Redis data | The delivery consumer |
| Status record | `Notifications:Status:RetentionHours` after its last write: 24 hours by default | Redis expiry |
| Idempotency record in flight | 30 seconds | Redis expiry |
| Idempotency record kept | 4 hours | Redis expiry |
| Template catalogue and settings | Until the next release or configuration change | The release or the operator |
| Graph email copy in Sent Items | The Microsoft 365 tenant's retention | The tenant, not this service |
| Log entries | The log platform's retention | The log platform. No personal data is in them |
