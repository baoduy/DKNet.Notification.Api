# DKNet.Notification.Api

DKNet Notification turns a registered template and caller parameters into one email or Microsoft Teams message for backend services.

Documentation reflects commit `362962797612943392d6ecb73686eed9c742a574`.

## 📖 Overview

- [Send a notification](docs/features/send-notification.md) — submit one rendered message for delivery; an accepted call may be queued or skipped.
- [Read notification status](docs/features/notification-status.md) — poll the caller's own notification until it succeeds or fails.

Backend services call this API; maintainers configure its templates and delivery channels; operators deploy it.

## 🏗️ Runtime architecture

![A backend caller gets an Entra ID token and calls the Notification API, which validates the JWT and the notifications.send permission; the API checks the idempotency record and status in Redis, counts the Redis delivery list against QueueCapacity (default 1,000), writes the pending status, publishes the message to that list and answers 200; the delivery consumer takes one message at a time, puts a not-due or retrying message back, writes the final status to Redis, and hands email to the selected sender — SMTP to the SMTP provider, or Graph, which signs in to Entra ID as the mail-sender app and posts to Microsoft Graph — and Teams messages to a Teams Workflows webhook.](docs/diagrams/runtime.svg)

The image was drawn from commit `e4769f8`; [runtime architecture](docs/runtime-architecture.md) records its evidence and comparison with the design.

## 🌐 Downstream systems

The delivery consumer sends email through the configured SMTP provider or Microsoft Graph, and Teams messages through a configured Teams Workflows webhook. Redis holds idempotency records, notification status, and the delivery list when `ConnectionStrings:Redis` is set. See the [operator guide](docs/operator-guide.md) for configuration.

## 🚀 Quick Start

With the .NET 10 SDK installed, clone the repository and run the API in Development mode using the checked-in launch profile:

```sh
git clone https://github.com/baoduy/DKNet.Notification.Api.git
cd DKNet.Notification.Api
dotnet run --project ApiEndpoints/DKNet.Notification.Api
```

The profile serves `http://localhost:5000`. From any directory, check the local process:

```sh
curl -i http://localhost:5000/healthz
```

Development settings disable authorization and enable email, but do not supply an SMTP host. Without a local SMTP configuration, a send call can return `200` with a status of `failed` because it was skipped. See the [send quick start](docs/features/send-notification.md) for the call.

## ✅ Verify it works

`GET /healthz` returns `200` with `{"status":"Healthy"}` when the host is healthy. This probe checks process liveness, not Redis or a delivery provider. Use the [deployment checks](docs/deployment.md#-verify) to verify a configured channel.

## 🛠️ Common commands

Run these from the repository root:

```sh
dotnet restore DKNet.Notification.sln
dotnet build DKNet.Notification.sln --no-restore --configuration Release
dotnet test DKNet.Notification.sln --no-build --configuration Release --verbosity minimal
```

The build and test commands match `.github/workflows/build.yml`; build before running the test command with `--no-build`.

## ⚠️ Known limitations

- A `200` send response confirms acceptance, not delivery. Poll status and inspect delivery logs.
- The shipped `account-opened` template has an email version in production settings; Teams needs a registered Teams version and a destination.
- `/healthz` does not check Redis, SMTP, Microsoft Graph, or Teams.

## 📚 Documentation

| Page | What it answers |
|---|---|
| [Send a notification](docs/features/send-notification.md) | Request, acceptance rules, queue and delivery flow |
| [Read notification status](docs/features/notification-status.md) | Caller scoped lookup and status lifecycle |
| [Deployment guide](docs/deployment.md) | Release path, install, verification and rollback |
| [Operator guide](docs/operator-guide.md) | Configuration, secrets, SMTP, Graph and Teams setup |
| [Runtime architecture](docs/runtime-architecture.md) | As built components and evidence |
| [Approved design](docs/architect/README.md) | Design intent and domain model |

## ❓ Open questions

| Question | Why it matters | Checked | Who can answer |
|---|---|---|---|
| Who owns operational support for this service? | Directs incident reports. | Repository README and chart | Project owner |
