# AGENTS.md

## Scope
- The solution is centered on `ApiEndpoints/` (six `DKNet.Notification.*` projects) with the solution file at the solution root. Every path below is relative to that root.
- Prefer code-verified patterns in this guide over older README statements when they differ.

## Architecture at a glance
- API startup is in `ApiEndpoints/DKNet.Notification.Api/Program.cs`: bind `FeatureOptions`, then `AddLogConfig` -> `AddAzureAppConfig` -> job dispatch (`JobSelector`: a `migration` argument runs `MigrationJob` and exits) -> `AddFluentValidationConfig` -> `RunMigrationAsync` (only when `FeatureManagement:RunDbMigrationWhenAppStart`) -> `AddAppConfig` -> `AddContextualRequestPopulation` -> `UseAppConfig(a => a.UseEndpointConfigs(...))`.
- Middleware/service composition is orchestrated by `DKNet.Notification.Api/Configs/AppConfig.cs` and `DKNet.Notification.Api/Configs/ServiceConfigs.cs`.
- Layer boundaries are strict: `Api` -> `AppServices` -> `Domains`, with infra wiring from `DKNet.Notification.Infra/Extensions/InfraSetup.cs`.
- `DKNet.Notification.AppHost/AppHost.cs` is Aspire host orchestration (Redis + PostgreSQL + API project), not business logic.
- Persistence uses EF Core with auto model config and seeding (`UseAutoConfigModel`, `UseAutoDataSeeding`) in `InfraSetup.AddInfraServices` **and** in `InfraMigration.MigrateDb` — both context-construction paths must wire seeding, or seed data never appears over HTTP depending on which startup path runs.

## Two worked feature patterns — pick one before copying
The template carries two side-by-side vertical slices demonstrating opposite ends of "hand-write it" vs "declare it and let the generator produce it". They differ layer by layer in what each writes and what the generated path gives up — most sharply: forwarded DataAnnotations validation on the automated sample is never evaluated under this template's own endpoint-registration convention.

- **Manual — `PurchaseOrder`** (`*/ManualSample/`): every layer hand-written — entity, event (`AddEvent` in the constructor), FluentValidation-backed create/update requests, business-rule rejection (`Cancel` on an already-cancelled order), filtered/paged list, request idempotency (`.RequiredIdempotentKey()`), static seed data. Endpoint: `DKNet.Notification.Api/ApiEndpoints/ManualSample/PurchaseOrderV1Endpoint.cs`.
- **Automated — `Product`** (`*/AutomatedSample/`): entity declares `[RaisesEvent(...)]` for its events, `[CrudCreate]`/`[CrudUpdate]` for its write operations, and a one-line `[GenerateDto(typeof(Product))]` DTO; `DKNet.SlimBus.Generators` produces the request/handler/route types (`CreateProductRequest`, `ChangePriceProductRequest`, their handlers, and `ProductCrudEndpointExtensions.MapProductCrud()`). Carries external Azure Service Bus publish/subscribe instead of idempotency/seeding. Endpoint: `DKNet.Notification.Api/ApiEndpoints/AutomatedSample/ProductV1Endpoint.cs` — composite, not generated-only: `MapProductCrud(o => …)` first, carrying per-route scopes and one `Exclude("Discontinue")`, then two hand-written business routes below it.

## Feature vertical slice pattern (copy this)
- Endpoint contract: implement `IEndpointConfig` (from `DKNet.AspCore.Extensions`) in `DKNet.Notification.Api/ApiEndpoints/**/*V1Endpoint.cs`.
- Hand-mapped routes use the raw dknet.notification-API surface (`group.MapPost(...)`, etc. — see `PurchaseOrderV1Endpoint`) or the package's generic entity helpers `MapGetList<TEntity,TKey,TDto>`/`MapGetById`/`MapPost<TRequest,TDto>`/`MapPutById`/`MapDeleteById` (see the generated `ProductCrudEndpointExtensions`).
- Manual write workflow example (`CreatePurchaseOrderRequest`): FluentValidation validator -> handler constructs the aggregate (which raises its own event via `AddEvent`) -> `repository.AddAsync` -> `mapper.ResultOf<TDto>(entity)`.
- Automated write workflow example (`Product`): `[CrudCreate]` constructor parameters become the generated request's properties 1:1; the generated handler calls `new Product(request.Name, request.Price)` and `repository.AddAsync` — no hand-written request, validator, or handler exists for it.
- Domain entity lives in `DKNet.Notification.Domains/Features/<Feature>/Entities` and keeps mutation in methods (`ChangeAmount(...)`, `Cancel(...)`, `ChangePrice(...)`).
- EF mapping lives in `DKNet.Notification.Infra/Features/<Feature>/Mappers` (`PurchaseOrderConfigs`, `ProductConfigs`) and enforces indexes/length/schema — hand-written for both samples; no generator produces `IEntityTypeConfiguration<T>`.

## Message bus and events
- `AddServiceBus` in `DKNet.Notification.Infra/Extensions/ServiceBusSetup.cs` always wires an in-memory child bus (`ImMemory`) for internal handlers.
- Azure Service Bus child bus (`AzureBus`) is added only when `ConnectionStrings:AzureBus` is non-empty; `Product`'s `Produce<ProductCreatedEvent>`/`Consume<ProductCreatedEvent>` wiring lives there.
- Domain events are published through `DKNet.Notification.Infra/Services/EventPublisher.cs` using `IMessageBus.Publish(...)`.
- Hand-raised example: `PurchaseOrderCreatedEvent`, raised by `AddEvent(...)` in `PurchaseOrder`'s constructor, consumed by `PurchaseOrderCreatedEventHandler`.
- Declared example: `[RaisesEvent(EventOperations.Created, Include = [...])]` / `[RaisesEvent(EventOperations.Updated, nameof(Price))]` on `Product` compose `ProductCreatedEvent`/`ProductPriceUpdatedEvent` at compile time — no `AddEvent` call anywhere in `AutomatedSample/`. Raised by DKNet's EF Core save hook, not by application code.

## Commands, mapping, and user context
- The acting user is populated onto `[FromClaim(ClaimTypes.Name)]`-decorated request properties by `AddContextualRequestPopulation` (wired in `Program.cs`) from the authenticated caller's claims; with `RequireAuthorization` off, that caller is the built-in demonstration authentication provider (`FeatureManagement:EnableDemoAuthentication`), never an unauthenticated fallback.
- A generated CRUD request (`CreateProductRequest`, `ChangePriceProductRequest`) can **never** carry an acting-user field — the generator forwards only `System.ComponentModel.DataAnnotations` attributes, not `[FromClaim]` (namespace `DKNet.AspCore.Extensions.ModelBinding`). `CreatedBy`/`CreatedOn` are instead stamped by `DKNet.EfCore.DataAuthorization`'s `DataOwnerHook`, wired once in `DKNet.Notification.Api/Configs/ServiceConfigs.cs` (`AddDataOwnerProvider<CoreDbContext, PrincipalProvider>()`) — it only fills in a blank `CreatedBy`, so the manual sample's constructor-set value (via `AggregateRoot(byUser)`) is left untouched.
- Mapster is global in `DKNet.Notification.AppServices/AppSetup.cs`; DTO generation uses `[GenerateDto(...)]` (example: `ProductDto`, one line, generates every audited property). The manual sample's `PurchaseOrderDto` is hand-written instead, exposing only the 5 fields it chooses.
- Lazy mapping result helpers are `DKNet.SlimBus.Extensions.LazyMapper`'s `ResultOf<T>`/`LazyMap<T>` (the template's former local copy under `AppServices/Extensions/LazyMapper` was removed — both samples use the package's version now).

## Build, run, and migration workflow
- SDK/framework are pinned centrally (`global.json`, `Directory.Packages.props`) and target `net10.0`.
- Core commands:
  - `dotnet restore`
  - `dotnet build -c Release`
  - `dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage"`
- Local host options:
  - API only: `dotnet run --project ApiEndpoints/DKNet.Notification.Api`
  - Aspire host: `dotnet run --project ApiEndpoints/DKNet.Notification.AppHost`
- EF migrations, run from `ApiEndpoints/`: `dotnet ef migrations add <Name> -c CoreDbContext -p DKNet.Notification.Infra/DKNet.Notification.Infra.csproj` and `dotnet ef migrations remove -c CoreDbContext -p DKNet.Notification.Infra/DKNet.Notification.Infra.csproj`.

## AI agent skills
- The `dknet-dknet.notification` plugin (Claude Code marketplace `baoduy/DKNet.Templates`, or `npx skills add baoduy/DKNet.Templates` for any agent) carries the full skill set: `dknet-project-structure` first, then `dknet-entity`, `dknet-efcore-config`, `dknet-crud`, `dknet-queries-specs`, `dknet-dto-mapping`, `dknet-endpoint`, `dknet-messaging-events`, `dknet-auth-and-ownership`, `dknet-platform-config`, `dknet-unit-tests`, `dknet-bdd-tests`; `/dknet-feature <Feature> <Entity> mode=manual|auto` scaffolds a slice end to end.

## Testing and quality constraints
- **Both suites are business-domain tests only.** Write tests for your entities, validators, specs, handlers, CRUD routes and domain events. Do not add tests for logging, telemetry, health probes, Swagger/OpenAPI documents, CORS, HSTS, security headers, rate limiting, JWT configuration, host startup plumbing or config binding — that is framework behaviour covered upstream, and it buries the examples a new team member reads to learn the conventions. The only non-business tests that belong here are the `Architecture/` layer rules.
- Tests currently live mainly under `ApiEndpoints/DKNet.Notification.App.Tests/` (Shouldly + xUnit patterns) and `ApiEndpoints/DKNet.Notification.App.BDDTests/` (Reqnroll + NUnit).
- `DKNet.Notification.App.Tests.csproj` disables analyzers for tests; production projects enforce strict warnings-as-errors from `Directory.Packages.props`.
- Coverage filters are defined in `coverage.runsettings`; avoid placing real logic in excluded paths (`bin/`, `obj/`, `*Test*.cs`).

## BDD Testing (Reqnroll + NUnit)
- BDD tests live in `ApiEndpoints/DKNet.Notification.App.BDDTests/`.
- `Support/BddApiFactory.cs` boots `WebApplicationFactory<Program>` once per test run using Reqnroll `[BeforeTestRun]` hook in `ApiHooks.cs`.
- In-memory EF Core + disabled migrations/AzureAppConfig — no external services required.
- Each scenario resets the DB in `[BeforeScenario(Order=0)]`; `HttpClient` and `ScenarioState` are injected into step defs via Reqnroll's BoDi `IObjectContainer`.
- Add new scenarios: create `.feature` under `Features/<Domain>/` and matching `[Binding]` step class under `Features/<Domain>/Steps/`.
- POST endpoints that opt into idempotency (the manual sample) require `X-Idempotency-Key: {Guid}` header — generate `Guid.NewGuid()` per request in `[When]` steps. The automated sample's generated create route has no such requirement.

## Project-specific gotchas
- `FeatureOptions` section name is `FeatureManagement`, and its JSON keys match the `FeatureOptions` property names one-for-one. `Get<FeatureOptions>()` ignores unknown keys, so a misspelled key silently falls back to the property default rather than throwing — when adding or renaming a flag, change `DKNet.Notification.Share/Options/FeatureOptions.cs` and every `appsettings*.json` together.
- Prefer adding new feature slices by mirroring `ManualSample/PurchaseOrder` (hand-written) or `AutomatedSample/Product` (generator-driven) across Domains -> Infra -> AppServices -> Api.
- When adding a service in Infra, register it explicitly in `InfraSetup.AddInfraServices` (there is no Scrutor scan — `IMembershipService` is an explicit `AddScoped` line); keep implementations `internal sealed` under `DKNet.Notification.Infra/Services/`.
- A generated CRUD request's DataAnnotations rules (e.g. `[Range]` on `Price`) are **not enforced** when the entity is mapped through the generated `MapProductCrud()`-style route, because .NET 10's automatic validation source generator can't see through `DKNet.AspCore.Extensions`'s generic `Map*<TRequest,TDto>` wrapper. Don't assume a `[Range]`/`[Required]` on a `[CrudCreate]` parameter is enforced without checking whether the entity's endpoint is hand-mapped (enforced) or generator-mapped (not enforced).
