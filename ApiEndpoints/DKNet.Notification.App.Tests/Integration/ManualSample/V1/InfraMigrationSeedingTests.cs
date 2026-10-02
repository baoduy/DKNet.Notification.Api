using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using DKNet.Notification.AppServices.ManualSample.V1;
using DKNet.Notification.Infra.Extensions;
using DKNet.Notification.Share;
using Testcontainers.PostgreSql;

namespace DKNet.Notification.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// Pins the <c>@integration Scenario: Seeded purchase-order reference data is available on a freshly started
/// application</c> acceptance point against the real code path that broke once already (DRK-722): no test
/// fixture in this repo wires <c>.UseAutoDataSeeding(...)</c> — neither the xUnit <see cref="Support.ApiFixture"/>
/// nor the BDD <c>BddApiFactory</c> (see <see cref="DKNet.Notification.App.Tests.Unit.ManualSample.PurchaseOrderStaticDataTests"/>) — that
/// wiring lives only in the real app's composition root, which is exactly what this test exercises: it runs
/// <see cref="InfraMigration.MigrateDb"/> itself — the exact method the real app calls at startup — against a
/// real, ephemeral Postgres container, then reads the result back over HTTP. It fails if
/// <c>.UseAutoDataSeeding(...)</c> is ever removed from <see cref="InfraMigration.MigrateDb"/>.
/// </summary>
public sealed class InfraMigrationSeedingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private WebApplicationFactory<DKNet.Notification.Api.Program>? _factory;

    #region Methods

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // The exact call InfraMigration.MigrateDb makes at real app startup (DbMigration.RunMigrationAsync) —
        // run directly here so the factory below never has to depend on Program.cs's Debug/Release migration
        // branching (Release mode requires a "migration" CLI arg and exits the process afterward).
        await InfraMigration.MigrateDb(_postgres.GetConnectionString());

        _factory = new WebApplicationFactory<DKNet.Notification.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AppDb"] = _postgres.GetConnectionString(),
                ["FeatureManagement:RunDbMigrationWhenAppStart"] = "false", // already migrated above
                ["FeatureManagement:RequireAuthorization"] = "false",
                ["FeatureManagement:EnableSwagger"] = "false",
                ["FeatureManagement:EnableAzureAppConfig"] = "false"
            }));
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task FreshlyMigratedRealDatabase_ShouldServeTheThreeSeededPurchaseOrders()
    {
        var client = _factory!.CreateClient();

        var response = await client.GetAsync("/v1/purchase-orders?pageIndex=1&pageSize=20");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var orders = await response.Content.ReadFromJsonAsync<List<PurchaseOrderDto>>(SharedConsts.JsonSerializerOptions);

        orders.ShouldNotBeNull();
        orders!.ShouldContain(o => o.CustomerName == "Acme Pte Ltd");
        orders.ShouldContain(o => o.CustomerName == "Globex Corporation");
        orders.ShouldContain(o => o.CustomerName == "Initech LLC");
    }

    #endregion
}
