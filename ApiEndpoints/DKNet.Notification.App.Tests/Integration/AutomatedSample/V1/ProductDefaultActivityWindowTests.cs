using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.AutomatedSample.V1;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;
using DKNet.Notification.Infra.Contexts;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// DRK-1173: on DKNet.AspCore.Extensions 10.1.20, a bare <c>GET /v1/products</c> over an audited entity is
/// bounded to <c>ListQueryOptions.DefaultActivityWindowMonths</c> (3) rather than all history. The BDD
/// harness's <c>ProductListSteps</c> only has an <see cref="HttpClient"/>, so it cannot back-date a seeded
/// record's <c>CreatedOn</c> at creation time — this test creates the record over HTTP (so
/// <c>DataOwnerHook</c> stamps ownership normally), then back-dates it as a plain <c>Modified</c>-state EF
/// update in a second scope, which — unlike an <c>Added</c> row — <c>CoreDbContext.EnsureOwnershipResolvable</c>
/// does not require an authenticated principal for (see its guard: it only inspects <c>Added</c> entries).
/// </summary>
public sealed class ProductDefaultActivityWindowTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    [Fact]
    public async Task List_ShouldExcludeARecordOlderThanTheDefaultWindow_UnlessFromDateReachesBackPastIt()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        var created = await (await client.PostAsJsonAsync("/v1/products", new { name = "Vintage Widget", price = 1.00m }))
            .Content.ReadFromJsonAsync<ProductDto>();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var product = await dbContext.Set<Product>().IgnoreQueryFilters().SingleAsync(p => p.Id == created!.Id);
            dbContext.Entry(product).Property(nameof(Product.CreatedOn)).CurrentValue =
                DateTimeOffset.UtcNow.AddMonths(-4);
            await dbContext.SaveChangesAsync();
        }

        var bareListing = await client.GetFromJsonAsync<JsonElement>("/v1/products");
        NamesOf(bareListing).ShouldNotContain("Vintage Widget",
            "a record older than the default 3-month activity window must not appear in a bare listing.");

        var fromDate = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMonths(-5).ToString("O"));
        var reachBackListing = await client.GetFromJsonAsync<JsonElement>($"/v1/products?fromDate={fromDate}");
        NamesOf(reachBackListing).ShouldContain("Vintage Widget",
            "naming a fromDate reaching back past the record's CreatedOn must surface it.");
    }

    private static IEnumerable<string> NamesOf(JsonElement envelope) =>
        envelope.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("name").GetString()!);
}
