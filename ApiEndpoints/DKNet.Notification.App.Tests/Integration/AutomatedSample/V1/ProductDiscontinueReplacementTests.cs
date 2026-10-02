using System.Net.Http.Json;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.AutomatedSample.V1;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// DRK-1386 §5: discontinuing a product is dropped from the generated CRUD group and hand-written, because
/// the business rule now spans two aggregates in one transaction — a product may not be discontinued
/// without naming its replacement, and discontinuing therefore discontinues one product and creates
/// another in the same request.
/// </summary>
public sealed class ProductDiscontinueReplacementTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    private const string TestScopesHeaderName = "X-Test-Scopes";

    [Fact]
    public async Task Discontinuing_AProduct_AlsoCreatesItsReplacement()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var widget = await CreateProductAsync(client, "Widget", 9.99m);

        using var request = WithScope(
            HttpMethod.Put, $"/v1/products/{widget.Id}/discontinue", "products.discontinue",
            new { replacementName = "Widget II", replacementPrice = 12.00m });
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var getWidgetResponse = await client.GetAsync($"/v1/products/{widget.Id}");
        var widgetDto = await getWidgetResponse.Content.ReadFromJsonAsync<ProductDto>();
        widgetDto!.IsDiscontinued.ShouldBeTrue();

        using var listResponse = await client.GetAsync("/v1/products?pageSize=100");
        var envelope = await listResponse.Content.ReadFromJsonAsync<ProductListEnvelope>();
        var replacement = envelope!.Items.SingleOrDefault(p => p.Name == "Widget II");
        replacement.ShouldNotBeNull("discontinuing must create the named replacement in the same transaction");
        replacement!.Price.ShouldBe(12.00m);

        // R6 — the acting user always comes from DataOwnerHook, never a caller-supplied payload field.
        replacement.CreatedBy.ShouldBe(TestAuthHandler.CallerProfileId.ToString());
    }

    [Fact]
    public async Task AProduct_CannotBeDiscontinued_WithoutNamingAReplacement()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var widget = await CreateProductAsync(client, "Widget", 9.99m);

        using var request = WithScope(
            HttpMethod.Put, $"/v1/products/{widget.Id}/discontinue", "products.discontinue", new { });
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var getWidgetResponse = await client.GetAsync($"/v1/products/{widget.Id}");
        var widgetDto = await getWidgetResponse.Content.ReadFromJsonAsync<ProductDto>();
        widgetDto!.IsDiscontinued.ShouldBeFalse();
    }

    [Fact]
    public async Task TheHandWrittenRoute_RefusesACallerWithoutItsScope()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var widget = await CreateProductAsync(client, "Widget", 9.99m);

        using var request = WithScope(
            HttpMethod.Put, $"/v1/products/{widget.Id}/discontinue", "products.write",
            new { replacementName = "Widget II", replacementPrice = 12.00m });
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var getWidgetResponse = await client.GetAsync($"/v1/products/{widget.Id}");
        var widgetDto = await getWidgetResponse.Content.ReadFromJsonAsync<ProductDto>();
        widgetDto!.IsDiscontinued.ShouldBeFalse();
    }

    private static async Task<ProductDto> CreateProductAsync(HttpClient client, string name, decimal price)
    {
        using var response = await client.PostAsJsonAsync("/v1/products", new { name, price });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static HttpRequestMessage WithScope(HttpMethod method, string url, string scope, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestScopesHeaderName, scope);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private sealed record ProductListEnvelope(List<ProductDto> Items);
}
