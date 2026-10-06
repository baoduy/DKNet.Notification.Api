# DKNet.Notification.Api
The notification service for DKNet banking platform

## 📘 Operator guide

[docs/operator-guide.md](docs/operator-guide.md) — deploy the service and set up SMTP, Microsoft Graph and Microsoft Teams delivery with the Helm chart.

## 🏗️ Runtime architecture

![A backend caller gets an Entra ID token and calls the Notification API, which validates the JWT and the notifications.send permission; the API checks the idempotency record and status in Redis, counts the Redis delivery list against QueueCapacity (default 1,000), writes the pending status, publishes the message to that list and answers 200; the delivery consumer takes one message at a time, puts a not-due or retrying message back, writes the final status to Redis, and hands email to the selected sender — SMTP to the SMTP provider, or Graph, which signs in to Entra ID as the mail-sender app and posts to Microsoft Graph — and Teams messages to a Teams Workflows webhook.](docs/diagrams/runtime.svg)

Drawn from the code at commit `e4769f8`. See [docs/runtime-architecture.md](docs/runtime-architecture.md) for the commit it was drawn from, file:line evidence, and how it compares to the [approved design](docs/architect/diagrams/runtime.svg).
