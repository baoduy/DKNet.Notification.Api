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
| Skipped | The outcome when the channel is unavailable: the call is accepted with 202, logged, and nothing is delivered. | Rejected. |
| Rejected | The outcome when the request is invalid: the call gets an error response and nothing is queued. | Skipped. |
| Delivery attempt | One try to hand the rendered message to the SMTP provider, Microsoft Graph or the Teams webhook. With Graph, the token request is part of the attempt. | A caller's retry of the API call. |
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
- **Persistence:** none. A notification lives only in memory, from the API call to its end state (ADR-0003).

## Domain events

None in version 1. No aggregate is stored, and no other service consumes an outcome. Each state change is one structured log entry instead (05-quality, Observability).

## Lifecycles

### Notification

| State | Meaning | Trigger to enter |
|---|---|---|
| Received | The API call passed authentication and the idempotency check. | `POST /v1/notifications` |
| Rejected (end) | The request is invalid, or the queue is full. Nothing is queued. | A validation, recipient or rendering check fails (400), or the queue is full (503). |
| Skipped (end) | The channel is unavailable. Nothing is delivered. | The channel is unsupported or not configured, the template has no version for it, or the Teams destination is not configured. |
| Queued | The rendered message waits in the delivery queue. The caller has its 202. | Rendering succeeded and the queue had room. |
| Delivering | The delivery worker runs one delivery attempt. | The worker takes the notification from the queue, or a retry wait ends. |
| Retry Waiting | A transient failure happened and attempts remain. | Transient failure with `AttemptCount` below 3. |
| Delivered (end) | The SMTP provider, Microsoft Graph or the Teams webhook accepted the message. | SMTP accepted the message, Graph answered 202, or the webhook answered 2xx. |
| Failed (end) | Delivery gave up. It is logged as an error. | Permanent failure, or a transient failure on attempt 3. |

A process stop loses every notification in Queued or Retry Waiting. No state records the loss (ADR-0003).

![Received moves to Rejected, Skipped or Queued; Queued moves to Delivering; Delivering ends Delivered or Failed, or waits in Retry Waiting and tries again.](diagrams/notification-lifecycle.svg)

`NotificationTemplate` has no lifecycle. It changes only with a new release.

## Domain model diagram

![NotificationTemplate holds one TemplateVersion per channel; Notification holds its parameters, recipient and rendered message, and refers to NotificationTemplate by template id only.](diagrams/domain-model.svg)
