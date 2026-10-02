# Runtime architecture — as built

Audience: maintainers of this repo. This page tracks what the service actually runs, drawn straight from the code, and names every place that differs from the approved design.

| | |
|---|---|
| **Drawn from** | `dev` at commit [`292459b`](https://github.com/baoduy/DKNet.Notification.Api/tree/292459bd6db2a44ad7e71ba45ff25bfe3c447846) — the merge of PR #3, slice 1 (scaffold) |
| **Diagram** | [`diagrams/runtime.architecture.json`](diagrams/runtime.architecture.json) (archify IR) · [`diagrams/runtime.svg`](diagrams/runtime.svg) (render) |
| **Compare against** | The approved design, revision 2: [`docs/architect/diagrams/runtime.architecture.json`](architect/diagrams/runtime.architecture.json) · [`docs/architect/diagrams/runtime.svg`](architect/diagrams/runtime.svg) |

![A backend caller reaches the Notification API scaffold host, which pipes every request through edge middleware into a rate limiter and an authorization gate that validates a Bearer JWT; the only route mapped today is the anonymous /healthz liveness check; Azure App Configuration, an OpenTelemetry exporter and a Redis-backed idempotency store are registered but not yet exercised by any business endpoint.](diagrams/runtime.svg)

Where a component in this diagram matches one in the design, it reuses the design's id (`api`, `redis`, `entra`) so the two compare item by item.

## What this commit actually runs

- **Notification API** host (`Program.cs:5-29`, `Configs/AppConfig.cs:15-19`) — no business endpoint is mapped yet.
- **Edge middleware** — forwarded headers, security headers, HTTPS/HSTS, CORS (`Configs/AppConfig.cs:83-88`, conditional registration at `Configs/AppConfig.cs:20-52`).
- **`/healthz`** — anonymous liveness probe, status only (`Configs/Healthz/HealthzConfig.cs:36-40`).
- **Rate limiter** — fixed-window + concurrency limiter (`Configs/RateLimits/RateLimitConfig.cs:27-49`, wired at `Configs/AppConfig.cs:93`).
- **Auth gate** — JWT Bearer with a default-deny fallback policy (`Configs/Auth/AuthConfig.cs:28-36`, wired at `Configs/AppConfig.cs:96`).
- **JWT Bearer authority** — OIDC metadata address from config, currently a placeholder tenant (`Configs/Auth/AuthConfig.cs:28-29`, `appsettings.json:31-39`).
- **Idempotency store** — registered, but no endpoint calls it yet (`Configs/AppConfig.cs:59-70`).
- **Redis** — backs both the idempotency store and the general distributed cache; falls back to in-memory for both when no connection string is set (`Configs/CacheConfig.cs:10-25`, `DKNet.Notification.AppHost/AppHost.cs:3-11`).
- **Azure App Configuration** — optional live config/feature-flag source, off by default (`Program.cs:10-11`, `Configs/AzureAppConfig/AzureAppConfigSetup.cs:20-48`).
- **OpenTelemetry exporter** — OTLP / Azure Monitor, off by default (`Program.cs:10`, `Configs/LogConfigs.cs:35-65`).

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

### In the code, not in the design

- **Azure App Configuration** as a live config/feature-flag source — `Program.cs:10-11`, `Configs/AzureAppConfig/AzureAppConfigSetup.cs:20-48`. Off by default (`FeatureManagement:EnableAzureAppConfig`).
- **OpenTelemetry exporter** (OTLP / Azure Monitor) — `Configs/LogConfigs.cs:35-65`. Off by default (`FeatureManagement:EnableOpenTelemetry`).
- **Anonymous `/healthz`** mapped ahead of routing, rate limiting and auth — `Configs/Healthz/HealthzConfig.cs:36-40`.
- **Rate limiter** (fixed window + concurrency) — `Configs/RateLimits/RateLimitConfig.cs:27-49`.
- **CORS, forwarded-headers, security-headers, antiforgery** edge middleware — `Configs/AppConfig.cs:83-88`.
- **Redis as a general distributed cache** (HybridCache / `StackExchangeRedisCache`), not only for idempotency — `Configs/CacheConfig.cs:10-25`.
- **In-memory fallback** for both the cache and the idempotency store when no Redis connection string is configured — `Configs/CacheConfig.cs:12-14`, `Configs/AppConfig.cs:66-69`.
- **Aspire AppHost** provisioning Redis for local runs — `DKNet.Notification.AppHost/AppHost.cs:3-11`.

### In both, but different

| | Design | Code |
|---|---|---|
| Identity provider | Microsoft Entra ID, named — also used for the Graph sender's client-credentials sign-in | Generic JWT Bearer authority from OIDC metadata, pointed at a placeholder tenant GUID; no client-credentials flow exists yet (`Configs/Auth/AuthConfig.cs:28-29`, `appsettings.json:31-39`) |
| Redis's job | Idempotency records only, behind a "password + TLS" boundary (ADR-0002) | Also the general HTTP response cache (`Configs/CacheConfig.cs:10-25`); nothing in the code asserts a password or TLS requirement, only a connection string |
| Authorization | Entra JWT **plus** the `notifications.send` scope | Default-deny fallback requires only an authenticated Bearer JWT; the scope policy exists but points at a placeholder `"sample-scope"` that no endpoint applies (`Configs/Auth/AuthConfig.cs:38-43`) |
| Port | `:8080` (design tag) | No production port is asserted in code; the only configured port is the dev-profile `http://localhost:5000` in `launchSettings.json` |
