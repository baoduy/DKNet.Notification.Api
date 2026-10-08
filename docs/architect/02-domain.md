# 02 — Domain

## Bounded context

**Notification Delivery** — turning one registered template and one caller's parameters into one message, and delivering it to one channel.

## Ubiquitous language

| Term | Meaning | Not to be confused with |
|---|---|---|
| Notification | One accepted request to deliver one message to one channel. | A domain event. This service publishes none. |
| Channel | The delivery route named in a request. Version 1 knows `email` and `teams`; revision 4 adds `telegram`. | A Teams channel inside a team, or a Telegram channel. Those are destinations. |
| Supported channel | A channel this release has a channel sender for. | A configured channel. |
| Configured channel | A supported channel whose settings are present and enabled in this deployment. For email, the selected email sender's required settings must be present too. For Telegram, the bot token must be present too. | A supported channel. |
| Email sender | The way this deployment hands email over: `Smtp` (an SMTP provider) or `Graph` (Microsoft Graph). Exactly one is active per deployment. | The sending mailbox. |
| Sending mailbox | The one Microsoft 365 mailbox the Graph email sender sends from, named in the settings. | The recipient. |
| Mail-sender app | The Entra app registration the Graph email sender signs in as. It holds `Mail.Send`, scoped to the sending mailbox. | The API's own app registration, which callers request tokens for. |
| Template | A registered message design, known by its template id. | A template version. |
| Template id | The stable name of a template, for example `account-opened`. | The file name of a template version. |
| Template version | The template's content for one channel: an HTML file for email, a Markdown file for Teams, a Telegram HTML file for Telegram. | A release version of the service. |
| Parameter | One key and string value sent by the caller. | A token. |
| Token | A `{{name}}` placeholder inside a template version, filled from the parameter of the same name. | A security token. |
| Recipient | Where the message goes. It is read from a reserved parameter key. | The caller. |
| Recipient key | The reserved parameter key for a channel: `to` for email, `teamsDestination` for Teams, `telegramDestination` for Telegram. | Any other parameter. |
| Teams destination | A name that operators map to one Teams Workflows webhook URL in the deployment settings. | The webhook URL itself. Callers never see it. |
| Telegram destination | A name that operators map to one Telegram chat id, a group or a channel, in the deployment settings. | The chat id itself. Callers never see it. |
| Bot | The one Telegram bot a deployment sends as. Its token is a secret setting. | A Teams bot. This service has none. |
| Telegram HTML | The subset of HTML Telegram parses with `parse_mode` = `HTML`. Templates may use `b`, `strong`, `i`, `em` and `a` (ADR-0017). | The email HTML, which allows any tag. |
| Visible text | A rendered Telegram message with its tags removed and its entities decoded. Its length is counted in UTF-16 code units. | The text as sent, with its tags. |
| Rendered message | The finished subject or title plus body, after every token is filled. A Telegram message has a body only. | The template version. |
| Skipped | The outcome when the channel is unavailable: the call is accepted with 200, logged, and nothing is delivered. | Rejected. |
| Rejected | The outcome when the request is invalid, or the queue is full: the call gets an error response and nothing is queued. | Skipped. |
| Public status | The value a caller reads for its notification: `pending`, `success` or `failed`. It folds the lifecycle states into 3 values (see Lifecycles). | The notification's internal state, which a caller never sees. |
| Delivery queue | The Redis list `notification-delivery`. Each message in it is one rendered notification that waits for an attempt (ADR-0013). | The in-memory bus that carries the API's own requests (ADR-0011). |
| Delivery attempt | One try to hand the rendered message to the SMTP provider, Microsoft Graph, the Teams webhook or the Telegram Bot API. With Graph, the token request is part of the attempt, and its answers follow the same transient and permanent rules. | A caller's retry of the API call. |
| Transient failure | A failure that may pass: a timeout, a lost connection, HTTP 408, 429 or 5xx, or an SMTP 4xx reply. | A permanent failure. |
| Permanent failure | A failure that will not pass: an SMTP 5xx reply, or HTTP 4xx other than 408 and 429. | A transient failure. |
| Caller | The backend system that sent the request, known by its token's `client_id`, `azp` or `appid` claim. | The recipient. |

## Aggregates

### NotificationTemplate

- **Root:** `NotificationTemplate`, identified by its template id.
- **Entities inside:** `TemplateVersion`, one per channel.
- **Value objects inside:**
  - `TemplateBody` — the file content and its format (`Html`, `Markdown` or `TelegramHtml`).
  - `SubjectLine` — the email subject pattern. It may hold tokens.
  - `CardTitle` — the optional Teams card title pattern. It may hold tokens.
- **Source:** a registration entry in `appsettings.json` plus one file per version in the `Templates` folder. Both ship inside the release.
- **Invariants:**
  - A template id is unique, is 1 to 100 characters, and holds only lowercase letters, digits and `-`.
  - A template has at least 1 version, and at most 1 version per channel.
  - An email version is `Html` and has a non-empty subject.
  - A Teams version is `Markdown`.
  - A Telegram version is `TelegramHtml`, with no subject and no title (ADR-0017). It:
    - uses only the tags `b`, `strong`, `i`, `em`, and `a` with only an `href` attribute, and closes every tag;
    - holds tokens in text only, never inside a tag;
    - holds at least 1 visible, non-white-space character outside its tokens.
  - Every registered file exists and is readable when the host starts. If not, the host fails to start.
  - The catalogue never changes while the process runs.
- **References:** none.

### Notification

- **Root:** `Notification`, identified by `NotificationId` (a new GUID per accepted call).
- **Value objects inside:**
  - `ChannelName` — the channel as the caller sent it, lower-cased.
  - `NotificationParameters` — a flat map of string keys to string values.
  - `Recipient` — `EmailRecipient` (one `EmailAddress`), `TeamsRecipient` (one `TeamsDestinationName`) or `TelegramRecipient` (one `TelegramDestinationName`).
  - `RenderedMessage` — subject or title, body, and body format.
  - `CallerId` — the caller's identity from its token.
- **State:** `Status` (see Lifecycles) and `AttemptCount`.
- **Invariants:**
  - A notification is rendered exactly once, before it is queued. Delivery never renders again.
  - A queued notification always has a recipient that is valid for its channel.
  - A queued notification has no token left unfilled.
  - A queued Telegram notification has a visible text of 1 to 4,096 UTF-16 code units.
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
| Skipped (end) | The channel is unavailable. Nothing is delivered. Its public status is `failed`. | The channel is unsupported or not configured, the template has no version for it, or the Teams or Telegram destination is not configured. |
| Queued | The rendered message waits in the delivery queue. The caller has its 200, and the status is `pending`. | Rendering succeeded and the queue had room. |
| Delivering | The delivery consumer runs one delivery attempt. | The consumer takes the notification from the queue once its `NotBefore` time has come, which includes the end of a retry wait. |
| Retry Waiting | A transient failure happened and attempts remain. The message waits in the queue until its `NotBefore` time. | Transient failure with `AttemptCount` below 3. |
| Delivered (end) | The SMTP provider, Microsoft Graph, the Teams webhook or the Telegram Bot API accepted the message. | SMTP accepted the message, Graph answered 202, the webhook answered 2xx, or Telegram answered 2xx with `ok` true. |
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

![NotificationTemplate holds one TemplateVersion per channel — email, Teams or Telegram; Notification holds its parameters, its one recipient (email address, Teams destination or Telegram destination) and its rendered message, and refers to NotificationTemplate by template id only.](diagrams/domain-model.svg)
