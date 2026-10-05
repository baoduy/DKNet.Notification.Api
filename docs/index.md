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
