using System.Net.Http.Json;
using DKNet.Notification.App.Tests.Integration.Support;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// DRK-1386 §5: <c>GET /v1/products/summary</c> is a hand-written route with no generated counterpart — a
/// read-only summary computed across every product the caller can see, discontinued included (Q1).
/// </summary>
public sealed class ProductPriceSummaryTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    private const string TestScopesHeaderName = "X-Test-Scopes";

    [Fact]
    public async Task Summary_ReportsProductCountAndAveragePrice_AcrossManyProducts()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        await CreateProductAsync(client, "Widget", 10.00m);
        await CreateProductAsync(client, "Gadget", 20.00m);

        using var request = WithScope(HttpMethod.Get, "/v1/products/summary", "products.read");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<ProductPriceSummaryResponse>();
        summary!.ProductCount.ShouldBe(2);
        summary.AveragePrice.ShouldBe(15.00m);
    }

    private static async Task CreateProductAsync(HttpClient client, string name, decimal price)
    {
        using var response = await client.PostAsJsonAsync("/v1/products", new { name, price });
        response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage WithScope(HttpMethod method, string url, string scope)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestScopesHeaderName, scope);
        return request;
    }

    private sealed record ProductPriceSummaryResponse(int ProductCount, decimal AveragePrice);
}
