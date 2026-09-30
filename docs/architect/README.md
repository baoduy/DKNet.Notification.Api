# DKNet Notification

DKNet Notification turns one registered template plus caller parameters into one finished message, and delivers it to email or Microsoft Teams for backend services.

| | |
|---|---|
| **Repo** | DKNet.Notification.Api — https://github.com/baoduy/DKNet.Notification.Api |
| **Service name** | DKNet.Notification |
| **Bounded context** | Notification Delivery |
| **Stack** | .NET 10 / ASP.NET Core minimal API with DKNet packages, scaffolded from DKNet.Templates (`dknet-minimal`), Aspire AppHost for local runs |
| **Status** | Active |
| **Design revision** | 1 |
| **Owner** | drunkcoding |
| **Root ticket** | DRK-1875 |

## Documents

- [01-scope.md](01-scope.md) — What problem does the service solve, for whom, and what does it never do?
- [02-domain.md](02-domain.md) — Which terms, aggregates, rules and states make up the model?
- [03-integration.md](03-integration.md) — Which API does it expose, what does it depend on, and how does a call flow?
- [04-data.md](04-data.md) — What does it store, where, and for how long?
- [05-quality.md](05-quality.md) — How is it secured, observed, tested, packaged and deployed?
- [adr/](adr/) — Why each major choice was made, and which options were rejected:
  - [ADR-0001](adr/0001-why-a-new-service.md) — Why a new service.
  - [ADR-0002](adr/0002-no-relational-database.md) — No relational database; Redis only for idempotency records.
  - [ADR-0003](adr/0003-accept-then-deliver-in-process.md) — Accept with 202, then deliver from an in-process queue.
  - [ADR-0004](adr/0004-template-rendering-with-dknet-transformation.md) — Render templates with DKNet.Svc.Transformation and `{{name}}` tokens.
  - [ADR-0005](adr/0005-email-over-smtp-with-mailkit.md) — Send email over SMTP with MailKit.
  - [ADR-0006](adr/0006-teams-through-workflows-webhooks.md) — Post to Teams through Workflows webhooks.
  - [ADR-0007](adr/0007-caller-authorization-scope-or-app-role.md) — Authorize callers by scope or app role.
- [diagrams/](diagrams/) — archify IR (`.json`) and render (`.svg`) for every diagram.

## Runtime architecture

![A backend caller gets an Entra ID token, posts to the Notification API, which checks the idempotency record in Redis, renders the template from the in-image catalogue, queues the message and returns 202; the delivery worker then sends it to the SMTP provider or a Teams Workflows webhook.](diagrams/runtime.svg)

## Delivery slices

Each slice is one future Workflow B ticket, delivered in this order.

1. **Scaffold** — generate the solution with `dotnet new dknet-minimal`, remove the two sample features, remove the relational database (ADR-0002), and add CI build and container publish. Realises: README, 04 Storage, 05 Packaging and deployment.
2. **Send API and template catalogue** — `POST /v1/notifications` with authorization, idempotency, the evaluation order, rendering, the skip rule, the delivery queue and the delivery worker. No channel sender exists yet, so every valid call ends Skipped. Realises: 02, 03 Exposed API and Main flows 1 and 2, 05 Security and Observability.
3. **Email channel** — the SMTP channel sender, its settings, the retry rule, and a local SMTP catcher (Mailpit) in the AppHost. Realises: 03 Main flow 3, ADR-0005.
4. **Microsoft Teams channel** — the Teams channel sender, named Teams destinations, the Adaptive Card payload and the retry rule. Realises: 03 Main flow 3, ADR-0006.
5. **Helm chart and operator guide** — a Helm chart like DKNet.Accounts.Api's, plus the configuration reference for channels, destinations and templates. Realises: 05 Packaging and deployment.
