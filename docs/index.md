---
title: DKNet Notification
---

DKNet Notification turns one registered template plus caller parameters into one finished message, and delivers it to email or Microsoft Teams for backend services.

Source: [baoduy/DKNet.Notification.Api](https://github.com/baoduy/DKNet.Notification.Api)

As-built pages describe commit `362962797612943392d6ecb73686eed9c742a574`.

## Run it

- [Send a notification](features/send-notification.md) — submit a templated message for email or Teams delivery.
- [Read notification status](features/notification-status.md) — poll the caller's own delivery outcome.
- [Deployment guide](deployment.md) — release path, install, verification and rollback.
- [Operator guide](operator-guide.md) — configuration and SMTP, Graph and Teams channel setup.

## How it is built

- [Runtime architecture — as built](runtime-architecture.md) — what the service actually runs today, and where it differs from the design.

## Approved design

- [Overview](architect/README.md) — service summary, runtime diagram and delivery slices.
- [01 — Scope](architect/01-scope.md) — the problem, who it is for, and what the service never does.
- [02 — Domain](architect/02-domain.md) — terms, aggregates, rules and states.
- [03 — Integration](architect/03-integration.md) — the exposed API, dependencies and call flows.
- [04 — Data](architect/04-data.md) — what is stored, where, and for how long.
- [05 — Quality attributes](architect/05-quality.md) — security, observability, testing, packaging and deployment.
