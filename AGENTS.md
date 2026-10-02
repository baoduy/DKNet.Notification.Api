# AGENTS.md

## Scope
- The solution is centered on `ApiEndpoints/` (`DKNet.Notification.*` projects) with the solution file at the solution root. Every path below is relative to that root.
- The service is an empty scaffold generated from the `dknet-minimal` template: it holds no feature yet, no database and no typed client. The approved design lives in `docs/architect/`.
- Prefer code-verified patterns in this guide over older README statements when they differ.

## Architecture at a glance
- API startup is in `ApiEndpoints/DKNet.Notification.Api/Program.cs`: bind `FeatureOptions`, then `AddLogConfig` -> `AddAzureAppConfig` -> `AddFluentValidationConfig` -> `AddAppConfig` -> `AddContextualRequestPopulation` -> `UseAppConfig(a => a.UseEndpointConfigs(...))`.
- Middleware/service composition is orchestrated by `DKNet.Notification.Api/Configs/AppConfig.cs` and `DKNet.Notification.Api/Configs/ServiceConfigs.cs`.
- Layer boundaries are strict: `Api` -> `AppServices` -> `Domains` -> `Share`.
- `DKNet.Notification.AppHost/AppHost.cs` is Aspire host orchestration (Redis + API project), not business logic.
- `GET /healthz` is the only route today: anonymous, status only (`{"status":"Healthy"}`), with no dependency check.
- Sign-in is Entra ID bearer tokens only (`AuthConfig`, JWT bearer). With `FeatureManagement:RequireAuthorization` on, every other request needs a valid token (fallback policy), including routes that do not exist. With it off (local Development, Testing) no sign-in middleware runs.

## Adding a feature
- Endpoint contract: implement `IEndpointConfig` (from `DKNet.AspCore.Extensions`) in `DKNet.Notification.Api/ApiEndpoints/**/*V1Endpoint.cs`; `UseEndpointConfigs` discovers it.
- Domain types live in `DKNet.Notification.Domains`, application services and validators in `DKNet.Notification.AppServices`.
- A command failure whose error carries a `PreconditionCodes.Prefix`-prefixed `"Code"` metadata entry answers 409 (`FluentValidationConfig`); every other failure keeps the library's status.
- Request idempotency comes from `DKNet.AspCore.Idempotency` (Redis store when `ConnectionStrings:Redis` is set, in-memory otherwise).

## Build and run
- SDK/framework are pinned centrally (`global.json`, `Directory.Packages.props`) and target `net10.0`.
- Core commands:
  - `dotnet restore`
  - `dotnet build -c Release`
  - `dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage"`
- Local host options:
  - API only: `dotnet run --project ApiEndpoints/DKNet.Notification.Api`
  - Aspire host: `dotnet run --project ApiEndpoints/DKNet.Notification.AppHost`
- No project in the solution is packable: `dotnet pack` writes no package.

## Testing and quality constraints
- Tests live under `ApiEndpoints/DKNet.Notification.App.Tests/` (Shouldly + xUnit) and `ApiEndpoints/DKNet.Notification.App.BDDTests/` (Reqnroll + NUnit). `ApiEndpoints/DKNet.Notification.App.TestSupport/` holds the shared host (`TestApiFactoryBase`) and the fake sign-in scheme (`TestAuthHandler`).
- Write business-domain tests for your entities, validators, handlers and routes. Do not add tests for logging, telemetry, Swagger/OpenAPI documents, CORS, HSTS, security headers or rate limiting — that is framework behaviour covered upstream. The non-business tests that belong here are the `Architecture/` layer rules and the `Scaffold/` acceptance tests of the empty service.
- `DKNet.Notification.App.Tests.csproj` disables analyzers for tests; production projects enforce strict warnings-as-errors from `Directory.Packages.props`.
- Coverage filters are defined in `coverage.runsettings`; avoid placing real logic in excluded paths (`bin/`, `obj/`, `*Test*.cs`).

## BDD Testing (Reqnroll + NUnit)
- `Support/BddApiFactory.cs` boots `WebApplicationFactory<Program>` once per test run using Reqnroll `[BeforeTestRun]` in `ApiHooks.cs`, with `RequireAuthorization` off — no external services required.
- `Support/TestOnlyRoutes.cs` registers routes that exist only in the test host (an unexpected error and a precondition failure) for the ErrorHandling scenarios.
- Each scenario clears captured logs in `[BeforeScenario(Order=0)]`; `HttpClient` and `ScenarioState` are injected into step defs via Reqnroll's BoDi `IObjectContainer`.
- Add new scenarios: create `.feature` under `Features/<Domain>/` and matching `[Binding]` step class under `Features/<Domain>/Steps/`.

## Project-specific gotchas
- `FeatureOptions` section name is `FeatureManagement`, and its JSON keys match the `FeatureOptions` property names one-for-one. `Get<FeatureOptions>()` ignores unknown keys, so a misspelled key silently falls back to the property default rather than throwing — when adding or renaming a flag, change `DKNet.Notification.Share/Options/FeatureOptions.cs` and every `appsettings*.json` together.
- `Program.cs` binds `FeatureOptions` before a `WebApplicationFactory` configuration override is merged in, so a test that must flip an early-bound flag (such as `RequireAuthorization`) sets the `FeatureManagement__<Flag>` environment variable instead (see `AuthOnApiFixture`).
