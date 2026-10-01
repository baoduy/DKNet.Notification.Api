# 01 — Scope

## Purpose

- Backend services in the DKNet banking platform need to tell people things: by email, or in a Microsoft Teams channel.
- Today no service can do this. No DKNet package sends email or posts to Teams.
- Without this service, every caller would build its own messages and hold its own email and Teams settings.
- DKNet Notification is the one place that owns message templates and channel delivery.
- A caller names a channel, a template and the parameters. The service builds the message and delivers it.

## Users and consumers

| Who | Kind | What they do |
|---|---|---|
| Backend services | Calling system | Call `POST /v1/notifications` with an Entra ID machine token. DKNet.Accounts.Api is a likely first caller; no caller is committed yet. |
| Template authors | Human (developer) | Add or change a template file and its registration, then release a new version of the service. |
| Operators | Human | Deploy the service, set channel settings and Teams destinations per deployment, and read logs and metrics. |
| Recipients | Human | Receive the email or read the Teams message. They never call the service. |

## Responsibilities

- Own the template catalogue: the registered templates and their version per channel.
- Validate each request: its shape, the template, the recipient and the parameters.
- Decide when a channel is unavailable, and then accept the call, log it and deliver nothing.
- Render the message from the template version and the parameters.
- Encode parameter values safely for the target format.
- Deliver the message to the channel, with a bounded retry on transient failures.
- Replay the first 202 to a repeated call with the same idempotency key from the same caller, and send nothing again.
- Hold the channel settings and Teams destinations for its deployment.
- Use exactly one email sender per deployment, SMTP or Microsoft Graph, picked in the settings.
- Log every outcome without personal data.

## Non-goals

| The service never… | Who does it instead |
|---|---|
| Decides when to notify or whom to notify | The calling service, for example DKNet.Accounts.Api |
| Subscribes to other services' events, such as DKNet.Accounts.Api's `ledger-events` queue | The calling service calls this API when it wants a message sent |
| Looks up a recipient's address from a user or customer id | The calling service passes the address in `parameters` |
| Tracks delivery status or keeps delivery history | No one in version 1; a later design revision |
| Accepts raw message text without a template | No one; the caller registers a template through a release |
| Manages templates at runtime | The release process of this repo |
| Generates PDFs | DKNet.Svc.PdfGenerators in the DKNet repo exists, and is not used in version 1 |
| Sends attachments | No one in version 1 |
| Schedules sends, handles opt-out, sends in bulk or localises templates | No one in version 1; the caller for bulk (one call per message) |
| Delivers to WhatsApp, Telegram, in-app or other channels | Later design revisions, one channel per change |
| Implements token replacement | DKNet.Svc.Transformation in the DKNet repo |
| Implements the idempotency store | DKNet.AspCore.Idempotency and its Redis store in the DKNet repo |
| Issues tokens or manages identities | Microsoft Entra ID |
| Runs a mail server or a mailbox | The SMTP provider set for the deployment, or Microsoft 365 for the Graph sender's mailbox |
| Keeps a copy of sent mail | No one with SMTP; the sending mailbox's Sent Items with Graph, under the tenant's retention |
| Grants or scopes its own mail permission | The tenant's Entra ID and Exchange Online administrators (05-quality, required setup step) |
| Keeps Teams webhooks alive | The Teams Workflows app; each workflow has a human owner |

## Boundaries

| Neighbour | Where the work splits |
|---|---|
| DKNet.Templates | It gives the starting solution shape. This service owns everything after the scaffold. |
| DKNet (packages) | The packages give token replacement, idempotency and error responses. This service owns the notification rules. |
| DKNet.Accounts.Api | Accounts decides that a customer must be told and calls this API. This service decides nothing about accounts. |
| Microsoft Entra ID | Entra ID issues and signs caller tokens. This service validates them and checks the scope or app role. |
| SMTP provider | The provider relays the email. This service builds and submits it. |
| Microsoft Graph and Microsoft 365 | Graph sends the email from the sending mailbox. This service builds the message and posts it to `sendMail`. |
| Microsoft Teams Workflows | A workflow posts the card into a channel. This service builds the card and posts it to the workflow's webhook. |
