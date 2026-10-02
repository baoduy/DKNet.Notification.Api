# Runtime architecture — as built

Audience: maintainers of this repo. This page tracks what the service actually runs, drawn straight from the code, and names every place that differs from the approved design.

All code paths below are repo-root relative, matching the IR's `sources` entries.

| | |
|---|---|
| **Drawn from** | `dev` at commit [`292459b`](https://github.com/baoduy/DKNet.Notification.Api/tree/292459bd6db2a44ad7e71ba45ff25bfe3c447846) — the merge of PR #3, slice 1 (scaffold) |
| **Diagram** | [`diagrams/runtime.architecture.json`](diagrams/runtime.architecture.json) (archify IR) · [`diagrams/runtime.svg`](diagrams/runtime.svg) (render) |
| **Compare against** | The approved design, revision 2: [`docs/architect/diagrams/runtime.architecture.json`](architect/diagrams/runtime.architecture.json) · [`docs/architect/diagrams/runtime.svg`](architect/diagrams/runtime.svg) |

![A backend caller reaches the Notification API scaffold host, which pipes every request through edge middleware, a rate limiter and an authorization gate that validates a Bearer JWT; the only route mapped today, the /healthz liveness check, sits behind that same gate but is allowed through anonymously; Azure App Configuration, an OpenTelemetry exporter and a Redis-backed idempotency store are registered but not yet exercised by any business endpoint.](diagrams/runtime.svg)

Where a component in this diagram matches one in the design, it reuses the design's id (`caller`, `api`, `redis`, `entra`) so the two compare item by item. The design has no equivalent for `edge`, `ratelimiter`, `authgate` or `idempotency` as separate ids — all four roll up into the design's single `api` component, and `idempotency`'s edge to `redis` rolls up into the design's `api-redis` edge.

## What this commit actually runs

- **Notification API** host (`ApiEndpoints/DKNet.Notification.Api/Program.cs:5-29`, `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:15-19`) — no business endpoint is mapped yet.
- **Edge middleware** — forwarded headers, security headers, HTTPS/HSTS, CORS (`ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:83-88`, conditional registration at `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:20-52`).
- **Rate limiter** — fixed-window + concurrency limiter, runs after routing and request bounds (`ApiEndpoints/DKNet.Notification.Api/Configs/RateLimits/RateLimitConfig.cs:27-49`, wired at `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:93`).
- **Auth gate** — JWT Bearer with a default-deny fallback policy, runs right after the rate limiter (`ApiEndpoints/DKNet.Notification.Api/Configs/Auth/AuthConfig.cs:28-36`, wired at `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:96`).
- **`/healthz`** — the endpoint is registered early (`ApiEndpoints/DKNet.Notification.Api/Configs/Healthz/HealthzConfig.cs:36-40`, mapped at `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:89`), but endpoint execution always happens after the full middleware pipeline — routing, request bounds, the rate limiter and the auth gate all run first (`ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:78-105`). `.AllowAnonymous()` is metadata the auth gate reads to let the request through unauthenticated; it does not skip the gate.
- **JWT Bearer authority** — OIDC metadata address from config, currently a placeholder tenant and audience (`ApiEndpoints/DKNet.Notification.Api/Configs/Auth/AuthConfig.cs:28-29`, `ApiEndpoints/DKNet.Notification.Api/appsettings.json:35-39`).
- **Idempotency store** — registered, but no endpoint calls it yet (`ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:59-70`).
- **Redis** — backs both the idempotency store and the general distributed cache; falls back to in-memory for both when no connection string is set (`ApiEndpoints/DKNet.Notification.Api/Configs/CacheConfig.cs:10-25`, `ApiEndpoints/DKNet.Notification.AppHost/AppHost.cs:3-11`).
- **Azure App Configuration** — optional live config/feature-flag source, off by default (`ApiEndpoints/DKNet.Notification.Api/Program.cs:10-11`, `ApiEndpoints/DKNet.Notification.Api/Configs/AzureAppConfig/AzureAppConfigSetup.cs:20-48`).
- **OpenTelemetry exporter** — OTLP / Azure Monitor, off by default (`ApiEndpoints/DKNet.Notification.Api/Program.cs:10`, `ApiEndpoints/DKNet.Notification.Api/Configs/LogConfigs.cs:35-65`).

## Connections

| Connection | `file:line` |
|---|---|
| `caller-api` | External caller — no code evidence in this repo |
| `api-appconfig` | `ApiEndpoints/DKNet.Notification.Api/Program.cs:10-11` |
| `api-telemetry` | `ApiEndpoints/DKNet.Notification.Api/Program.cs:10` |
| `api-edge` | `ApiEndpoints/DKNet.Notification.Api/Program.cs:23-24`, `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:83` |
| `edge-ratelimiter` | `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:89-93` |
| `ratelimiter-authgate` | `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:93,96` |
| `authgate-entra` | `ApiEndpoints/DKNet.Notification.Api/Configs/Auth/AuthConfig.cs:28-29` |
| `authgate-healthz` | `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:78-105`, `ApiEndpoints/DKNet.Notification.Api/Configs/Healthz/HealthzConfig.cs:36-40` |
| `api-idempotency` | `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:59-70` |
| `idempotency-redis` | `ApiEndpoints/DKNet.Notification.Api/Configs/CacheConfig.cs:10-25` |

## Differences from the design

### In the design, not in the code yet

| Design piece | Ships in | Design reference |
|---|---|---|
| `POST /v1/notifications`, the `notifications.send` scope rule, idempotency's non-default settings | Slice 2 — Send API, template catalogue and skip rule | `docs/architect/README.md` → Delivery slices |
| Delivery queue, delivery worker | Slice 3 — Email channel, rendering and delivery | same |
| SMTP sender, SMTP provider | Slice 3 | same |
| Graph sender, Microsoft Graph, its client-credentials sign-in as the mail-sender app | Slice 4 — Graph email sender | same |
| Teams sender, Teams Workflows webhook | Slice 5 — Microsoft Teams channel | same |
| Caller → Entra "get token" | n/a | Token acquisition happens on the caller's side; this repo has no code evidence for it |

### In the code, not in the design's runtime diagram

Each of these is already described in the design's prose — just not drawn in its runtime diagram:

- **Azure App Configuration** as a live config/feature-flag source — `ApiEndpoints/DKNet.Notification.Api/Program.cs:10-11`, `ApiEndpoints/DKNet.Notification.Api/Configs/AzureAppConfig/AzureAppConfigSetup.cs:20-48`. Off by default (`FeatureManagement:EnableAzureAppConfig`). Design prose: `docs/architect/04-data.md:26`.
- **OpenTelemetry exporter** (OTLP / Azure Monitor) — `ApiEndpoints/DKNet.Notification.Api/Configs/LogConfigs.cs:35-65`. Off by default (`FeatureManagement:EnableOpenTelemetry`). Design prose: `docs/architect/05-quality.md:118`.
- **Anonymous `/healthz`** — `ApiEndpoints/DKNet.Notification.Api/Configs/Healthz/HealthzConfig.cs:36-40`. Design prose: `docs/architect/03-integration.md:25`.
- **Aspire AppHost** provisioning Redis for local runs — `ApiEndpoints/DKNet.Notification.AppHost/AppHost.cs:3-11`. Design prose: `docs/architect/05-quality.md:149`.

Not mentioned in the design at all:

- **Rate limiter** (fixed window + concurrency) — `ApiEndpoints/DKNet.Notification.Api/Configs/RateLimits/RateLimitConfig.cs:27-49`.
- **Request bounds** — a pipeline stage between routing and the rate limiter: request timeout, max body size, header timeout (`ApiEndpoints/DKNet.Notification.Api/Configs/RequestBoundsConfig.cs:26-68`, wired at `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:92`, on by default via `appsettings.json:24-27`).
- **CORS, forwarded-headers, security-headers, antiforgery** edge middleware — `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:83-88`.
- **Redis as a general distributed cache** (HybridCache / `StackExchangeRedisCache`), not only for idempotency — `ApiEndpoints/DKNet.Notification.Api/Configs/CacheConfig.cs:10-25`.
- **In-memory fallback** for both the cache and the idempotency store when no Redis connection string is configured — `ApiEndpoints/DKNet.Notification.Api/Configs/CacheConfig.cs:12-14`, `ApiEndpoints/DKNet.Notification.Api/Configs/AppConfig.cs:66-69`.

### In both, but different

| | Design | Code |
|---|---|---|
| Identity provider | Microsoft Entra ID, named — also used for the Graph sender's client-credentials sign-in | A placeholder tenant and audience (`ApiEndpoints/DKNet.Notification.Api/appsettings.json:35-39`); no client-credentials flow exists yet |
| Redis's job | Idempotency records only, behind a "password + TLS" boundary (ADR-0002) | Also the general HTTP response cache (`ApiEndpoints/DKNet.Notification.Api/Configs/CacheConfig.cs:10-25`); nothing in the code asserts a password or TLS requirement, only a connection string |
| Authorization | Entra JWT **plus** the `notifications.send` scope | Default-deny fallback requires only an authenticated Bearer JWT; the scope policy exists but points at a placeholder `"sample-scope"` that no endpoint applies (`ApiEndpoints/DKNet.Notification.Api/Configs/Auth/AuthConfig.cs:38-43`) |
| Port | `:8080` (design tag) | No production port is asserted in code; the only configured port is the dev-profile `http://localhost:5000` (`ApiEndpoints/DKNet.Notification.Api/Properties/launchSettings.json:10`) |
