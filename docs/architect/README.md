# DKNet Notification

DKNet Notification turns one registered template plus caller parameters into one finished message, and delivers it to email, Microsoft Teams or Telegram for backend services.

| | |
|---|---|
| **Repo** | DKNet.Notification.Api — https://github.com/baoduy/DKNet.Notification.Api |
| **Service name** | DKNet.Notification |
| **Bounded context** | Notification Delivery |
| **Stack** | .NET 10 / ASP.NET Core minimal API with DKNet packages, scaffolded from DKNet.Templates (`dknet-minimal`), Aspire AppHost for local runs |
| **Status** | Active |
| **Design revision** | 4 |
| **Owner** | drunkcoding |
| **Root ticket** | DRK-1875 (revision 1), DRK-1961 (revision 2), DRK-2137 (revision 3), DRK-2173 (revision 4) |

## Documents

- [01-scope.md](01-scope.md) — What problem does the service solve, for whom, and what does it never do?
- [02-domain.md](02-domain.md) — Which terms, aggregates, rules and states make up the model?
- [03-integration.md](03-integration.md) — Which API does it expose, what does it depend on, and how does a call flow?
- [04-data.md](04-data.md) — What does it store, where, and for how long?
- [05-quality.md](05-quality.md) — How is it secured, observed, tested, packaged and deployed? What does the client package ship as?
- [adr/](adr/) — Why each major choice was made, and which options were rejected:
  - [ADR-0001](adr/0001-why-a-new-service.md) — Why a new service.
  - [ADR-0002](adr/0002-no-relational-database.md) — No relational database; Redis only for idempotency records. Amended by ADR-0012 and ADR-0013: Redis also holds delivery and status.
  - [ADR-0003](adr/0003-accept-then-deliver-in-process.md) — Accept with 202, then deliver from an in-process queue. Superseded by ADR-0013.
  - [ADR-0004](adr/0004-template-rendering-with-dknet-transformation.md) — Render templates with DKNet.Svc.Transformation and `{{name}}` tokens.
  - [ADR-0005](adr/0005-email-over-smtp-with-mailkit.md) — Send email over SMTP with MailKit. Its rejected Graph alternative is superseded by ADR-0009.
  - [ADR-0006](adr/0006-teams-through-workflows-webhooks.md) — Post to Teams through Workflows webhooks.
  - [ADR-0007](adr/0007-caller-authorization-scope-or-app-role.md) — Authorize callers by scope or app role.
  - [ADR-0008](adr/0008-idempotency-caller-scoped-replay.md) — Idempotency keys scoped by caller, repeated calls replayed.
  - [ADR-0009](adr/0009-graph-email-sender-one-per-deployment.md) — Add a Microsoft Graph email sender; one email sender per deployment.
  - [ADR-0010](adr/0010-graph-sign-in-separate-app-workload-identity.md) — Sign in to Graph as a separate app with workload identity; `Mail.Send` scoped to one mailbox.
  - [ADR-0011](adr/0011-slimmessagebus-in-process-mediator.md) — Use SlimMessageBus's in-memory bus as the in-process mediator; validation stays outside it.
  - [ADR-0012](adr/0012-notification-status-tracking.md) — Let a caller read the status of its own notification: `pending`, `success` or `failed`; the accept answer becomes 200.
  - [ADR-0013](adr/0013-delivery-through-slimmessagebus-redis-queue.md) — Deliver through the SlimMessageBus Redis queue, so waiting notifications survive a restart.
  - [ADR-0014](adr/0014-typed-client-package.md) — Ship a typed .NET client package, DKNet.Notification.Client, released with the service.
  - [ADR-0015](adr/0015-client-uses-refit.md) — The client package uses Refit for its HTTP calls; the API never takes Refit.
  - [ADR-0016](adr/0016-telegram-through-bot-api-one-bot-per-deployment.md) — Send to Telegram through the Bot API: one bot per deployment, operator-named destinations, and a bot token that never leaves the request.
  - [ADR-0017](adr/0017-telegram-html-format-escaped-values.md) — Telegram templates use Telegram HTML with 5 tags; values are escaped; the visible text is counted against 4,096.
- [diagrams/](diagrams/) — archify IR (`.json`) and render (`.svg`) for every diagram.

## Runtime architecture

![A backend caller, through plain HTTPS or the DKNet.Notification.Client package, gets an Entra ID token and posts across the service edge to the Notification API inside the per-replica container; the API checks the idempotency record in Redis, which sits outside the container and is shared by all replicas, renders, writes the pending status, publishes the message to the delivery list in Redis and answers 200, and answers the caller's status lookup from Redis; the delivery consumer takes messages from that list, puts a message back to wait for a retry, writes the final status, and hands email to the one active email sender — SMTP to the SMTP provider, or Microsoft Graph with a token for the mail-sender app — posts Teams cards to a Teams Workflows webhook, and posts Telegram messages to the Telegram Bot API as the deployment's one bot.](diagrams/runtime.svg)

## Delivery slices

Each slice is one future Workflow B ticket, delivered in this order.

Slices 1 to 6 are built and released as version 1. Revision 3 adds slices 7 and 8. Revision 4 adds slices 9 and 10.

1. **Scaffold** — generate the solution with `dotnet new dknet-minimal`, remove the two sample features, remove the relational database (ADR-0002), and add CI build and container publish. Realises: README, 04 Storage, 05 Packaging and deployment.
2. **Send API, template catalogue and skip rule** — `POST /v1/notifications` with authorization, idempotency with its three non-default settings (ADR-0008), evaluation steps 1 to 5, the template catalogue and its start-up checks, and the skip log entry. No channel sender exists yet, so every valid call ends Skipped at step 5; that is the behaviour this slice ships and tests. Realises: 02 NotificationTemplate, 03 Exposed API, Evaluation order steps 1 to 5 and Main flow 2, 05 Security and Observability, ADR-0007, ADR-0008.
3. **Email channel, rendering and delivery** — the email recipient check, rendering with HTML encoding, the delivery queue and delivery worker with the retry rule, the `Sender` setting with `Smtp` as its only sender, the SMTP channel sender and its settings, the missing-setting rule with its two start-up log entries, and a local SMTP catcher (Mailpit) in the AppHost. Email is the first channel that reaches steps 6 to 10, so rendering, the queue and the worker ship with it. Realises: 02 Notification lifecycle, 03 Evaluation order steps 6 and 8 to 10 and Main flow 1, 04 EmailChannelSettings and SmtpSenderSettings, ADR-0003, ADR-0004, ADR-0005.
4. **Graph email sender** — `Sender` = `Graph`, the Graph sender and its settings, sign-in with both credential modes as the mail-sender app, the `sendMail` call, the retry classification for the token step and Graph answers, and the Graph stub tests. It reuses the queue and worker from slice 3. Realises: 03 Outbound call — Microsoft Graph `sendMail` and Main flow 4, 04 GraphSenderSettings, 05 Graph sign-in and the Graph rows of Testing approach, ADR-0009, ADR-0010.
5. **Microsoft Teams channel** — the Teams recipient check, named Teams destinations, the Adaptive Card payload with its 28 KB check, and the Teams channel sender with 429 handling. It reuses the queue and worker from slice 3. Realises: 03 Evaluation order steps 6 to 8 for Teams and Main flow 3, ADR-0006.
6. **Helm chart and operator guide** — a Helm chart like DKNet.Accounts.Api's, with the email sender setting, the workload identity service account annotation and the `azure.workload.identity/use` pod label, plus the configuration reference for channels, email senders, destinations and templates. The operator guide carries the Graph setup: the mail-sender app, its federated credential, and the required `Mail.Send` scope to the one mailbox with RBAC for Applications (or an application access policy), checked with `Test-ServicePrincipalAuthorization`. Realises: 05 Graph mailbox scope — required setup step, Packaging and deployment.
7. **Client package** (dev-team) — the `DKNet.Notification.Client` project: a Refit route declaration for `POST /v1/notifications` and `GET /v1/notifications/{notificationId}`, its own request and response types, the two registrations (base address; base address plus the caller's own `DelegatingHandler`), the one refusal exception, the README inside the package, and the route parity and package tests. Nothing for `/healthz`. The API does not change. The method shapes and error detail come from the client spec (DRK-2136). Realises: 03 Context map and Dependencies, 05 Packaging and deployment (NuGet package) and the client rows of Testing approach, ADR-0014, ADR-0015.
8. **Client release** (devops, CI/CD) — the release workflow packs `DKNet.Notification.Client` with the release version it computes for the image and pushes it to GitHub Packages, as DKNet.Accounts.Api's release workflow does. Needs slice 7. The client is not released until this slice merges. Realises: 05 Packaging and deployment (CI, NuGet package), ADR-0014.
9. **Telegram channel** (dev-team) — the `telegram` channel: the `telegramDestination` recipient check, named Telegram destinations, the Telegram settings with the missing-setting rule and its start-up warning, the `TelegramHtml` template format with its start-up checks, value escaping, the 4,096 visible-character check, the Telegram sender with its retry classification and `retry_after` wait, the token rules for logs and traces, and the Telegram stub tests. It reuses the queue and consumer from slice 3. No caller and no client package changes. Realises: 02 Telegram terms and the `TelegramRecipient` value object, 03 Evaluation order steps 5 to 8 for Telegram, Outbound call — Telegram `sendMessage` and Main flow 5, 04 TelegramChannelSettings and TelegramDestination, 05 Content safety, Secrets, Logs and the Telegram rows of Testing approach, ADR-0016, ADR-0017.
10. **Telegram Helm values and operator guide** (devops) — the chart's Telegram plain values (off by default), the opt-in Key Vault secret for the bot token kept out of the default secret list, a commented destination example, and the chart version bump. The operator guide carries the Telegram setup: create the bot with BotFather, add it to each group (or to each channel as an administrator that can post messages), read each chat id, store the token, set a new chat id after a group becomes a supergroup, and turn Telegram on only after every replica runs the release of slice 9. Needs slice 9. Realises: 05 Packaging and deployment (Helm chart, rollout rule) and Telegram set-up — required steps, ADR-0016.
