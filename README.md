# DKNet.Notification.Api
The notification service for DKNet banking platform

## 📘 Operator guide

[docs/operator-guide.md](docs/operator-guide.md) — deploy the service and set up SMTP, Microsoft Graph and Microsoft Teams delivery with the Helm chart.

## 🏗️ Runtime architecture

![A backend caller reaches the Notification API scaffold host, which pipes every request through edge middleware, a rate limiter and an authorization gate that validates a Bearer JWT; the only route mapped today, the /healthz liveness check, sits behind that same gate but is allowed through anonymously; Azure App Configuration, an OpenTelemetry exporter and a Redis-backed idempotency store are registered but not yet exercised by any business endpoint.](docs/diagrams/runtime.svg)

Drawn from the code at commit `292459b` (the scaffold slice). See [docs/runtime-architecture.md](docs/runtime-architecture.md) for the commit it was drawn from, file:line evidence, and how it compares to the [approved design](docs/architect/diagrams/runtime.svg).
