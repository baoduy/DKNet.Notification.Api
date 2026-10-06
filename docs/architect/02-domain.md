# 02 — Domain

## Bounded context

**Notification Delivery** — turning one registered template and one caller's parameters into one message, and delivering it to one channel.

## Ubiquitous language

| Term | Meaning | Not to be confused with |
|---|---|---|
| Notification | One accepted request to deliver one message to one channel. | A domain event. This service publishes none. |
| Channel | The delivery route named in a request. Version 1 knows `email` and `teams`. | A Teams channel inside a team. That is a Teams destination. |
| Supported channel | A channel this release has a channel sender for. | A configured channel. |
| Configured channel | A supported channel whose settings are present and enabled in this deployment. For email, the selected email sender's required settings must be present too. | A supported channel. |
| Email sender | The way this deployment hands email over: `Smtp` (an SMTP provider) or `Graph` (Microsoft Graph). Exactly one is active per deployment. | The sending mailbox. |
| Sending mailbox | The one Microsoft 365 mailbox the Graph email sender sends from, named in the settings. | The recipient. |
| Mail-sender app | The Entra app registration the Graph email sender signs in as. It holds `Mail.Send`, scoped to the sending mailbox. | The API's own app registration, which callers request tokens for. |
| Template | A registered message design, known by its template id. | A template version. |
| Template id | The stable name of a template, for example `account-opened`. | The file name of a template version. |
| Template version | The template's content for one channel: an HTML file for email, a Markdown file for Teams. | A release version of the service. |
| Parameter | One key and string value sent by the caller. | A token. |
| Token | A `{{name}}` placeholder inside a template version, filled from the parameter of the same name. | A security token. |
| Recipient | Where the message goes. It is read from a reserved parameter key. | The caller. |
| Recipient key | The reserved parameter key for a channel: `to` for email, `teamsDestination` for Teams. | Any other parameter. |
| Teams destination | A name that operators map to one Teams Workflows webhook URL in the deployment settings. | The webhook URL itself. Callers never see it. |
| Rendered message | The finished subject or title plus body, after every token is filled. | The template version. |
| Skipped | The outcome when the channel is unavailable: the call is accepted with 200, logged, and nothing is delivered. | Rejected. |
| Rejected | The outcome when the request is invalid, or the queue is full: the call gets an error response and nothing is queued. | Skipped. |
| Public status | The value a caller reads for its notification: `pending`, `success` or `failed`. It folds the lifecycle states into 3 values (see Lifecycles). | The notification's internal state, which a caller never sees. |
| Delivery queue | The Redis list `notification-delivery`. Each message in it is one rendered notification that waits for an attempt (ADR-0013). | The in-memory bus that carries the API's own requests (ADR-0011). |
| Delivery attempt | One try to hand the rendered message to the SMTP provider, Microsoft Graph or the Teams webhook. With Graph, the token request is part of the attempt, and its answers follow the same transient and permanent rules. | A caller's retry of the API call. |
| Transient failure | A failure that may pass: a timeout, a lost connection, HTTP 408, 429 or 5xx, or an SMTP 4xx reply. | A permanent failure. |
| Permanent failure | A failure that will not pass: an SMTP 5xx reply, or HTTP 4xx other than 408 and 429. | A transient failure. |
| Caller | The backend system that sent the request, known by its token's `client_id`, `azp` or `appid` claim. | The recipient. |

## Aggregates

### NotificationTemplate

- **Root:** `NotificationTemplate`, identified by its template id.
- **Entities inside:** `TemplateVersion`, one per channel.
- **Value objects inside:**
  - `TemplateBody` — the file content and its format (`Html` or `Markdown`).
  - `SubjectLine` — the email subject pattern. It may hold tokens.
  - `CardTitle` — the optional Teams card title pattern. It may hold tokens.
- **Source:** a registration entry in `appsettings.json` plus one file per version in the `Templates` folder. Both ship inside the release.
- **Invariants:**
  - A template id is unique, is 1 to 100 characters, and holds only lowercase letters, digits and `-`.
  - A template has at least 1 version, and at most 1 version per channel.
  - An email version is `Html` and has a non-empty subject.
  - A Teams version is `Markdown`.
  - Every registered file exists and is readable when the host starts. If not, the host fails to start.
  - The catalogue never changes while the process runs.
- **References:** none.

### Notification

- **Root:** `Notification`, identified by `NotificationId` (a new GUID per accepted call).
- **Value objects inside:**
  - `ChannelName` — the channel as the caller sent it, lower-cased.
  - `NotificationParameters` — a flat map of string keys to string values.
  - `Recipient` — `EmailRecipient` (one `EmailAddress`) or `TeamsRecipient` (one `TeamsDestinationName`).
  - `RenderedMessage` — subject or title, body, and body format.
  - `CallerId` — the caller's identity from its token.
- **State:** `Status` (see Lifecycles) and `AttemptCount`.
- **Invariants:**
  - A notification is rendered exactly once, before it is queued. Delivery never renders again.
  - A queued notification always has a recipient that is valid for its channel.
  - A queued notification has no token left unfilled.
  - `AttemptCount` is never above 3.
  - A notification changes state only along the lifecycle below.
  - Parameter values and the recipient are never written to a log.
- **References:** `NotificationTemplate` by template id only.
- **Persistence:** none for the aggregate itself. Its queued form is a `DeliverNotification` message in the Redis delivery list, and its public status is a record beside it (04-data; ADR-0012, ADR-0013). `Notification.Resume` rebuilds the aggregate from a queued message for each attempt: Queued when no attempt was made, Retry Waiting otherwise. `Resume` refuses an attempt count of 3 or more, so the 3-attempt limit stays in the domain. A resumed notification holds the rendered message and the recipient, not the original parameters.

## Domain events

None in version 1. No other service consumes an outcome. Each state change is one structured log entry instead (05-quality, Observability). The caller reads the end state from the status record, not from an event.

## Lifecycles

### Notification

| State | Meaning | Trigger to enter |
|---|---|---|
| Received | The API call passed authentication and the idempotency check. | `POST /v1/notifications` |
| Rejected (end) | The request is invalid, or the queue is full. Nothing is queued and no status is written. | A validation, recipient or rendering check fails (400), or the queue is full (503). |
| Skipped (end) | The channel is unavailable. Nothing is delivered. Its public status is `failed`. | The channel is unsupported or not configured, the template has no version for it, or the Teams destination is not configured. |
| Queued | The rendered message waits in the delivery queue. The caller has its 200, and the status is `pending`. | Rendering succeeded and the queue had room. |
| Delivering | The delivery consumer runs one delivery attempt. | The consumer takes the notification from the queue once its `NotBefore` time has come, which includes the end of a retry wait. |
| Retry Waiting | A transient failure happened and attempts remain. The message waits in the queue until its `NotBefore` time. | Transient failure with `AttemptCount` below 3. |
| Delivered (end) | The SMTP provider, Microsoft Graph or the Teams webhook accepted the message. | SMTP accepted the message, Graph answered 202, or the webhook answered 2xx. |
| Failed (end) | Delivery gave up. It is logged as an error. | Permanent failure, or a transient failure on attempt 3. |

A process stop loses no waiting notification in Queued or Retry Waiting, apart from at most the one message being taken from the list at that instant: the message is in Redis, and a replica that is cut off during an attempt puts it back without counting the attempt (ADR-0013). A hard crash can lose the one message a replica holds. No state records such a loss, and the status stays `pending` until its record expires (05-quality).

The public status folds these states into 3 values. Only `pending` is not final.

| Public `status` | Internal states | Final |
|---|---|---|
| `pending` | Queued, Delivering, Retry Waiting | No |
| `success` | Delivered | Yes |
| `failed` | Failed, Skipped | Yes |

A status never goes backwards: `pending` is written before the message is published, and only final values are written after it.

![Received moves to Rejected, Skipped or Queued; Queued moves to Delivering; Delivering ends Delivered or Failed, or waits in Retry Waiting and tries again.](diagrams/notification-lifecycle.svg)

`NotificationTemplate` has no lifecycle. It changes only with a new release.

## Domain model diagram

![NotificationTemplate holds one TemplateVersion per channel; Notification holds its parameters, recipient and rendered message, and refers to NotificationTemplate by template id only.](diagrams/domain-model.svg)
