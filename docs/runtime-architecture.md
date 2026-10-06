# Runtime architecture — as built

Audience: maintainers of this repo. This page tracks what the service actually runs, drawn straight from the code, and names every place that differs from the approved design.

All code paths below are repo-root relative, matching the IR's `sources` entries.

| | |
|---|---|
| **Drawn from** | `dev` at commit [`e4769f8`](https://github.com/baoduy/DKNet.Notification.Api/tree/e4769f873896277e98aed1d8ebc6523b6746b1bb), with every slice shipped (send, status, email over SMTP and Graph, Teams, Redis delivery queue). The `Notifications:Delivery:QueueCapacity` entries in `appsettings.json` and the Helm values came in the change after it |
| **Diagram** | [`diagrams/runtime.architecture.json`](diagrams/runtime.architecture.json) (archify IR) · [`diagrams/runtime.svg`](diagrams/runtime.svg) (render) |
| **Compare against** | The approved design, revision 2: [`docs/architect/diagrams/runtime.architecture.json`](architect/diagrams/runtime.architecture.json) · [`docs/architect/diagrams/runtime.svg`](architect/diagrams/runtime.svg) |

![A backend caller gets an Entra ID token and calls the Notification API, which validates the JWT and the notifications.send permission; the API checks the idempotency record and status in Redis, counts the Redis delivery list against QueueCapacity (default 1,000), writes the pending status, publishes the message to that list and answers 200; the delivery consumer takes one message at a time, puts a not-due or retrying message back, writes the final status to Redis, and hands email to the selected sender — SMTP to the SMTP provider, or Graph, which signs in to Entra ID as the mail-sender app and posts to Microsoft Graph — and Teams messages to a Teams Workflows webhook.](diagrams/runtime.svg)

Every component reuses the design's id (`caller`, `entra`, `api`, `redis`, `queue`, `worker`, `graphsender`, `smtpsender`, `teams`, `graph`, `smtp`, `webhook`), and every connection reuses the design's connection id, so the two compare item by item. The topology is the same; the differences are in what each piece does, listed below.

## What this commit actually runs

- **Notification API** host (`ApiEndpoints/DKNet.Notification.Api/Program.cs:4-28`). Services are registered in `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:16-89`; the pipeline runs in `AppConfig.cs:102-138` in this order: forwarded headers, security headers, antiforgery, CORS, HTTPS/HSTS, `/healthz`, routing, request bounds, rate limiter, authentication and authorization, endpoints.
- **Routes** — `POST /v1/notifications` and `GET /v1/notifications/{notificationId}` (`ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/Notifications/NotificationsV1Endpoint.cs:33-44`). The body filter runs before `.RequiredIdempotentKey()` (`:38-40`), so a refused body holds no key. `/healthz` is anonymous and liveness only (`ApiEndpoints/DKNet.Notification.Api/Configs/Healthz/HealthzConfig.cs:15-17`, `:36-40`).
- **Sign-in** — JWT bearer with a fallback policy that needs a signed-in caller, and the `notifications.send` policy on the whole group (`ApiEndpoints/DKNet.Notification.Api/Configs/Auth/AuthConfig.cs:23-47`, `NotificationsV1Endpoint.cs:15`). With `FeatureManagement:RequireAuthorization` off (Development, Testing) no sign-in middleware runs (`AppConfig.cs:27-30`).
- **Mediator bus** — endpoint to handler on SlimMessageBus's memory provider (`ApiEndpoints/DKNet.Notification.Api/Configs/ServiceConfigs.cs:45-51`).
- **Accepting a call** — `SendNotificationService` renders from the template catalogue loaded once from the image's `Templates` folder (`ApiEndpoints/DKNet.Notification.Api/Configs/TemplateConfig.cs:20-31`), then `QueueAsync` (`ApiEndpoints/DKNet.Notification.AppServices/Notifications/SendNotificationService.cs:214-271`): counts the delivery list, refuses with `QUEUE_FULL` at `QueueCapacity` (`:223-227`), writes `pending` (`:238-242`), publishes (`:243-260`). `QUEUE_FULL` answers 503 with `Retry-After: 30` (`ApiEndpoints/DKNet.Notification.Api/Configs/FluentValidationConfig.cs:33-34`, `NotificationsV1Endpoint.cs:79-82`).
- **Delivery list** — a Redis list named `notification-delivery` on the `Delivery` child bus, consumed with `Instances(1)` (`ServiceConfigs.cs:53-82`, queue name at `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliverNotification.cs:47`). The API counts it with `LLEN` (`ApiEndpoints/DKNet.Notification.Api/Configs/RedisDeliveryBacklog.cs:10-11`).
- **Queue limit** — `Notifications:Delivery:QueueCapacity`, default 1,000, allowed 1–100,000, for the whole service (`ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliverySettings.cs:14-51`), bound in `ApiEndpoints/DKNet.Notification.Api/Configs/EmailConfig.cs:18`. It is set in `ApiEndpoints/DKNet.Notification.Api/appsettings.json:91-93` and `helm/dknet-notification/values.yaml:138`.
- **Delivery consumer** — one attempt per message; a not-due or retrying message goes back to the list with a not-before time; the final status is written at the end (`ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryConsumer.cs:54-135`, `:204-239`). Retries: 5 s then 30 s, at most 3 attempts (`DeliverySettings.cs:14-17`); a 429 waits for `Retry-After`, at most 60 s (`ApiEndpoints/DKNet.Notification.AppServices/Delivery/RetryAfterWait.cs:9`).
- **Status store** — `status:{callerId}:{notificationId}` in `IDistributedCache` for `Notifications:Status:RetentionHours` (default 24); a failed write logs and is dropped (`ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusStore.cs:31`, `:40-58`).
- **Senders** — `Notifications:Email:Sender` picks Graph or SMTP (`EmailConfig.cs:32-45`). SMTP uses MailKit with STARTTLS or TLS (`ApiEndpoints/DKNet.Notification.AppServices/Delivery/SmtpEmailSender.cs:37-82`, `:115-118`). Graph gets a token as the mail-sender app, by workload identity or a client secret, then posts `sendMail` (`ApiEndpoints/DKNet.Notification.AppServices/Delivery/GraphSignIn.cs:21-54`, `GraphEmailSender.cs:67-99`). Teams posts the card to the destination's HTTPS webhook (`ApiEndpoints/DKNet.Notification.AppServices/Delivery/TeamsWebhookSender.cs:59-89`).
- **Redis** — idempotency store (`AppConfig.cs:64-66`), distributed and hybrid cache, which also holds the status records (`ApiEndpoints/DKNet.Notification.Api/Configs/CacheConfig.cs:16-25`), and the delivery list.
- **Azure App Configuration** — optional, off by default (`ApiEndpoints/DKNet.Notification.Api/Configs/AzureAppConfig/AzureAppConfigSetup.cs:20-48`, `appsettings.json:49`).
- **OpenTelemetry** — logs, traces and the `DKNet.Notification` meter, including the `notifications.queue.length` gauge; off by default (`ApiEndpoints/DKNet.Notification.Api/Configs/LogConfigs.cs:34-78`, `appsettings.json:51`, gauge registered at `AppConfig.cs:110-111`).
- **Aspire AppHost** (local runs) — Redis and Mailpit (SMTP 1025 with STARTTLS, inbox 8025) (`ApiEndpoints/DKNet.Notification.AppHost/AppHost.cs:3-31`).

## Connections

| Connection | `file:line` |
|---|---|
| `caller-token` | Caller side — no code evidence in this repo |
| `caller-post` | `ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/Notifications/NotificationsV1Endpoint.cs:33-44` |
| `api-keys` | `ApiEndpoints/DKNet.Notification.Api/Configs/Auth/AuthConfig.cs:28-29` |
| `api-redis` | `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:64-66`, `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusStore.cs:40-70` |
| `api-queue` | `ApiEndpoints/DKNet.Notification.AppServices/Notifications/SendNotificationService.cs:223-260` |
| `queue-worker` | `ApiEndpoints/DKNet.Notification.Api/Configs/ServiceConfigs.cs:71-80`, `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryConsumer.cs:139-154` |
| `worker-redis` | `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryConsumer.cs:235-238` |
| `worker-graph`, `worker-smtp`, `worker-teams` | `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryConsumer.cs:82-87`, `ApiEndpoints/DKNet.Notification.Api/Configs/EmailConfig.cs:32-45` |
| `graph-token` | `ApiEndpoints/DKNet.Notification.AppServices/Delivery/GraphSignIn.cs:21-54` |
| `graph-send` | `ApiEndpoints/DKNet.Notification.AppServices/Delivery/GraphEmailSender.cs:52`, `:67-99` |
| `smtp-send` | `ApiEndpoints/DKNet.Notification.AppServices/Delivery/SmtpEmailSender.cs:61-67` |
| `teams-webhook` | `ApiEndpoints/DKNet.Notification.AppServices/Delivery/TeamsWebhookSender.cs:59-89` |

## Differences from the design

### In the code, not in the design's runtime diagram

- **Memory fallback without Redis** — idempotency, cache, status and the delivery bus all run in memory when `ConnectionStrings:Redis` is not set (`AppConfig.cs:68-72`, `CacheConfig.cs:12-15`, `ServiceConfigs.cs:30-33`, `:55-66`). Only Development and Testing may do this; any other environment fails at start-up (`AppConfig.cs:73-79`). The in-process backlog always counts 0 (`ApiEndpoints/DKNet.Notification.AppServices/Delivery/IDeliveryBacklog.cs:13-17`), so **`QUEUE_FULL` never fires in a local run without Redis**.
- **Rate limiter** (fixed window + concurrency, 429) — `ApiEndpoints/DKNet.Notification.Api/Configs/RateLimits/RateLimitConfig.cs:19-53`, on in the base settings (`appsettings.json:48`).
- **Request bounds** (body size, header timeout, request timeout answering 504) — `ApiEndpoints/DKNet.Notification.Api/Configs/RequestBoundsConfig.cs:26-68`.
- **Edge middleware**: forwarded headers, security headers, antiforgery, CORS, HTTPS/HSTS — `AppConfig.cs:117-121`.
- **Azure App Configuration** and **OpenTelemetry** — both optional and off by default (see above). Design prose: `docs/architect/04-data.md`, `docs/architect/05-quality.md`.
- **Up to three Redis connections** per replica: the idempotency store's own, the cache's, and one shared by the delivery bus and the backlog count (`ServiceConfigs.cs:36-39`).

### In both, but different

| | Design | Code |
|---|---|---|
| Redis boundary | "Data store: password + TLS" | Nothing asserts a password or TLS; the connection string is used as given (`ServiceConfigs.cs:38`, `:73`, `CacheConfig.cs:20`, `AppConfig.cs:64-66`). Helm reads it from Key Vault. |
| `notifications.send` | Entra JWT with `notifications.send` in `scp`, `scope` or `roles` | Same, and it also needs a caller claim (`client_id`, `azp` or `appid`) — `ApiEndpoints/DKNet.Notification.Api/Configs/Auth/SendPermission.cs:20-25`, `ApiEndpoints/DKNet.Notification.AppServices/Share/CallerIdentity.cs:28-40` |
| Identity provider | Microsoft Entra ID, named | The base settings still carry a placeholder tenant and audience (`appsettings.json:31-42`); each deployment sets its own |
| Only the selected sender is created | One email sender | SMTP is still registered when Graph is chosen with bad settings — harmless, because email is then skipped (`EmailConfig.cs:32-45`). The Teams sender is always registered (`ApiEndpoints/DKNet.Notification.Api/Configs/TeamsConfig.cs:21`) |
| Port | `:8080` (design tag) | No code asserts a port; 8080 is the container image default, set only in Helm (`helm/dknet-notification/values.yaml:105`) |
| Queue limit | "1,000 in all" | `QueueCapacity`, default 1,000, configurable 1–100,000 (`DeliverySettings.cs:14-51`) |

### Code notes found while drawing

- `Program.cs:6-7` reads `FeatureOptions` before `AddAzureAppConfig` (`:10`), though its comment says "after". Feature flags held in App Configuration therefore never change start-up wiring.
- `/healthz` does not check Redis (`HealthzConfig.cs:15-17`): a replica that has lost Redis still reports Healthy.
