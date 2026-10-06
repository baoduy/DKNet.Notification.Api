# AGENTS.md

## Scope
- The solution is centered on `ApiEndpoints/` (`DKNet.Notification.*` projects) with the solution file at the solution root. Every path below is relative to that root.
- The service sends email and Microsoft Teams notifications from registered templates, keeps each notification's status for its caller and delivers through a SlimMessageBus Redis queue. It was generated from the `dknet-minimal` template and has no database. Its typed .NET client, `DKNet.Notification.Client` (Refit, ADR-0014, ADR-0015), has its own contracts and references no project of the solution; only test projects reference both the API and the client. The approved design lives in `docs/architect/`.
- Prefer code-verified patterns in this guide over older README statements when they differ.

## Architecture at a glance
- API startup is in `ApiEndpoints/DKNet.Notification.Api/Program.cs`: bind `FeatureOptions`, then `AddLogConfig` -> `AddAzureAppConfig` -> `AddFluentValidationConfig` -> `AddAppConfig` -> `AddContextualRequestPopulation` -> `UseAppConfig(a => a.UseEndpointConfigs(...))`.
- Middleware/service composition is orchestrated by `DKNet.Notification.Api/Configs/AppConfig.cs` and `DKNet.Notification.Api/Configs/ServiceConfigs.cs`.
- Layer boundaries are strict: `Api` -> `AppServices` -> `Domains` -> `Share`.
- `DKNet.Notification.AppHost/AppHost.cs` is Aspire host orchestration (Redis + API project), not business logic.
- API routes: `POST /v1/notifications` and `GET /v1/notifications/{notificationId}` (`NotificationsV1Endpoint`, both need `notifications.send`), and `GET /healthz`: anonymous, status only (`{"status":"Healthy"}`), with no dependency check. With `EnableSwagger` on (local Development) the OpenAPI document and `/docs` are served too.
- `POST` answers `200 OK { "notificationId" }` for a queued or a skipped call, with no `Location` header. `GET` answers the caller's own `pending`, `success` or `failed` with `Cache-Control: no-store`; an unknown, expired or another caller's id answers 404 `NOTIFICATION_NOT_FOUND`. A skipped call reads `failed`.
- Delivery runs through `DeliveryConsumer` (`AppServices/Delivery`), a SlimMessageBus consumer on the queue `notification-delivery` with `Instances(1)`: a Redis list when `ConnectionStrings:Redis` is set, the memory provider in local runs and tests (`ServiceConfigs`). It makes one attempt per message and publishes a not-yet-due or retrying message back to the queue; no exception leaves it, because the Redis consumer would drop the message. `QueueCapacity` counts that list for the whole service.
- `NotificationStatusStore` keeps the status under `status:{callerId}:{notificationId}` in `IDistributedCache` for `Notifications:Status:RetentionHours` (default 24, 1–168). A write is best effort: a failure logs and never fails the call or the delivery. `SendNotificationService` writes `pending` before it publishes.
- Sign-in is Entra ID bearer tokens only (`AuthConfig`, JWT bearer). With `FeatureManagement:RequireAuthorization` on, every other request needs a valid token (fallback policy), including routes that do not exist. With it off (local Development, Testing) no sign-in middleware runs.

## Adding a feature
- Endpoint contract: implement `IEndpointConfig` (from `DKNet.AspCore.Extensions`) in `DKNet.Notification.Api/ApiEndpoints/**/*V1Endpoint.cs`; `UseEndpointConfigs` discovers it.
- Domain types live in `DKNet.Notification.Domains`, application services and validators in `DKNet.Notification.AppServices`.
- An endpoint sends a command (`Fluents.Requests.IWitResponse<T>` from `DKNet.SlimBus.Extensions`) on SlimMessageBus's in-memory bus to an `internal sealed` handler in `AppServices`; `AddServicesFromAssembly` in `ServiceConfigs` registers it (ADR-0011). Keep body validation in an endpoint filter registered before `.RequiredIdempotentKey()`, not in the bus, so a refused body holds no idempotency key.
- A command failure whose error carries a `PreconditionCodes.Prefix`-prefixed `"Code"` metadata entry answers 409 (`FluentValidationConfig`); every other failure keeps the library's status.
- Time: production reads and waits on the injected `TimeProvider` (registered as `TimeProvider.System` in `ServiceConfigs`), never `DateTimeOffset.UtcNow` or a plain `Task.Delay`. Unit tests drive a `FakeTimeProvider` and move it only after the consumer has put the message back (see `DeliveryConsumerTests`). The exceptions are the sender I/O time limits and the `Retry-After` date, which stay on the real clock because they are about a real provider; BDD runs on the real clock.
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
- `DKNet.Notification.Client` is the only packable project: `dotnet pack ApiEndpoints/DKNet.Notification.Client -c Release` writes its package, with `README.md` at the package root. Its version comes from the release tags; never hand-edit one.

## Testing and quality constraints
- Tests live under `ApiEndpoints/DKNet.Notification.App.Tests/` (Shouldly + xUnit) and `ApiEndpoints/DKNet.Notification.App.BDDTests/` (Reqnroll + NUnit). `ApiEndpoints/DKNet.Notification.App.TestSupport/` holds the shared host (`TestApiFactoryBase`) and the fake sign-in scheme (`TestAuthHandler`).
- Write business-domain tests for your entities, validators, handlers and routes. Do not add tests for logging, telemetry, Swagger/OpenAPI documents, CORS, HSTS, security headers or rate limiting — that is framework behaviour covered upstream. The non-business tests that belong here are the `Architecture/` layer rules. What is measured and what is not is under "Code coverage".
- `DKNet.Notification.App.Tests.csproj` disables analyzers for tests; production projects enforce strict warnings-as-errors from `Directory.Packages.props`.
- App.Tests classes run in parallel. A class that listens to something process-wide (an Azure SDK event source, a diagnostic or activity listener for every source) or asserts a tight time bound joins `[Collection(SerialTestsCollection.Name)]`, which runs alone after the rest.

## Code coverage
- Coverage and unit tests are for the application only: the `DKNet.Notification.*` projects that serve the API and hold the business rules (today `Api`, `AppServices`, `Domains`, `Share`) and the `Client` package. `coverage.runsettings` includes `[DKNet.Notification.*]*`, so a new application project is measured without a change.
- Not measured, and no tests written for them: the test projects, `App.TestSupport`, the Aspire `AppHost` (local runs only), EF Core migrations (`**/Migrations/**`, model snapshot included) and generated code. A new local-run-only project (a second host, a migration runner, a seeding tool) gets an assembly entry in the `<Exclude>` list of `coverage.runsettings` in the same change that adds it.
- Framework wiring in `Api/Configs/` carries `[ExcludeFromCodeCoverage]` on the class: every `*Config` class (enforced by `Architecture/ApiTests`) and the plumbing types beside them (rate limiting, antiforgery, security headers, Swagger, options binding). Never put the attribute on a type that decides business behaviour — an endpoint, filter, handler, validator, domain type or the caller-permission check (`Configs/Auth`) — test it instead. Keep business decisions out of `*Config` classes; the one that has one (`FluentValidationConfig`'s error-to-status mapping) is covered by the BDD error scenarios.
- Never place real logic in an excluded path (`bin/`, `obj/`, `*Test*.cs`, `Migrations/`) or an excluded project.

## BDD Testing (Reqnroll + NUnit)
- `Support/BddApiFactory.cs` boots `WebApplicationFactory<Program>` once per test run using Reqnroll `[BeforeTestRun]` in `ApiHooks.cs`, with `RequireAuthorization` off — no external services required.
- `Support/TestOnlyRoutes.cs` registers routes that exist only in the test host (an unexpected error and a precondition failure) for the ErrorHandling scenarios.
- Each scenario clears captured logs in `[BeforeScenario(Order=0)]`; `HttpClient` and `ScenarioState` are injected into step defs via Reqnroll's BoDi `IObjectContainer`.
- Add new scenarios: create `.feature` under `Features/<Domain>/` and matching `[Binding]` step class under `Features/<Domain>/Steps/`.
- Features run in parallel (`AssemblyInfo.cs`, 4 workers); the scenarios of one feature run one at a time. `RedisServer` and `MailCatcher` keep one container per feature (`FeatureKey`), so a flush or clear stays inside its feature. Do not add mutable static state shared across features; key it by `FeatureKey.Current` instead.

## Project-specific gotchas
- `FeatureOptions` section name is `FeatureManagement`, and its JSON keys match the `FeatureOptions` property names one-for-one. `Get<FeatureOptions>()` ignores unknown keys, so a misspelled key silently falls back to the property default rather than throwing — when adding or renaming a flag, change `DKNet.Notification.Share/Options/FeatureOptions.cs` and every `appsettings*.json` together.
- `Program.cs` binds `FeatureOptions` before a `WebApplicationFactory` configuration override is merged in, so a test that must flip an early-bound flag (such as `RequireAuthorization`) sets the `FeatureManagement__<Flag>` environment variable instead. That variable is process-wide and tests run in parallel, so such a test must join `SerialTestsCollection` (App.Tests) or be `[NonParallelizable]` (BDD).
