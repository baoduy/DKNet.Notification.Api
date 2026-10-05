---
title: DKNet Notification
---

DKNet Notification turns one registered template plus caller parameters into one finished message, and delivers it to email or Microsoft Teams for backend services.

Source: [baoduy/DKNet.Notification.Api](https://github.com/baoduy/DKNet.Notification.Api)

## Run it

- [Operator guide](operator-guide.md) — deploy on Kubernetes with the Helm chart and turn on email (SMTP or Microsoft Graph) and Teams delivery.

## How it is built

- [Runtime architecture — as built](runtime-architecture.md) — what the service actually runs today, and where it differs from the design.

## Approved design

- [Overview](architect/README.md) — service summary, runtime diagram and delivery slices.
- [01 — Scope](architect/01-scope.md) — the problem, who it is for, and what the service never does.
- [02 — Domain](architect/02-domain.md) — terms, aggregates, rules and states.
- [03 — Integration](architect/03-integration.md) — the exposed API, dependencies and call flows.
- [04 — Data](architect/04-data.md) — what is stored, where, and for how long.
- [05 — Quality attributes](architect/05-quality.md) — security, observability, testing, packaging and deployment.

### Architecture decision records

- [ADR-0001](architect/adr/0001-why-a-new-service.md) — Why a new service.
- [ADR-0002](architect/adr/0002-no-relational-database.md) — No relational database; Redis only for idempotency records.
- [ADR-0003](architect/adr/0003-accept-then-deliver-in-process.md) — Accept with 202, then deliver from an in-process queue.
- [ADR-0004](architect/adr/0004-template-rendering-with-dknet-transformation.md) — Render templates with DKNet.Svc.Transformation and `{{name}}` tokens.
- [ADR-0005](architect/adr/0005-email-over-smtp-with-mailkit.md) — Send email over SMTP with MailKit.
- [ADR-0006](architect/adr/0006-teams-through-workflows-webhooks.md) — Post to Teams through Workflows webhooks.
- [ADR-0007](architect/adr/0007-caller-authorization-scope-or-app-role.md) — Authorize callers by scope or app role.
- [ADR-0008](architect/adr/0008-idempotency-caller-scoped-replay.md) — Idempotency keys scoped by caller, repeated calls replayed.
- [ADR-0009](architect/adr/0009-graph-email-sender-one-per-deployment.md) — Add a Microsoft Graph email sender; one email sender per deployment.
- [ADR-0010](architect/adr/0010-graph-sign-in-separate-app-workload-identity.md) — Sign in to Graph as a separate app with workload identity.
- [ADR-0011](architect/adr/0011-slimmessagebus-in-process-mediator.md) — Use SlimMessageBus's in-memory bus as the in-process mediator.
