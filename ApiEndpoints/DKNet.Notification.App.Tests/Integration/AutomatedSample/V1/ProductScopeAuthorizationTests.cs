using System.Net.Http.Json;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.AutomatedSample.V1;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// DRK-1386 §5: the product sample's generated CRUD routes each carry their own authorization scope
/// (<c>products.read</c>/<c>products.write</c>), except the supplier-reference route, which keeps a
/// scope of its own (<c>products.supplier</c>). The discriminating <c>Given</c> for every scenario here is
/// the <c>X-Test-Scopes</c> request header — the caller's scope claim for that one request, replacing
/// <see cref="TestAuthHandler" />'s all-scopes default (row 19 of the DRK-1387 brief).
/// </summary>
public sealed class ProductScopeAuthorizationTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    private const string TestScopesHeaderName = "X-Test-Scopes";

    [Fact]
    public async Task AReadScope_IsEnoughToReadAProduct()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var widget = await CreateProductAsync(client, "Widget", 9.99m);

        using var request = WithScope(HttpMethod.Get, $"/v1/products/{widget.Id}", "products.read");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ProductDto>();
        dto!.Name.ShouldBe("Widget");
    }

    [Fact]
    public async Task AReadScope_IsNotEnoughToCreateAProduct()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var request = WithScope(
            HttpMethod.Post, "/v1/products", "products.read",
            new { name = "Gadget", price = 5.00m });
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var listRequest = WithScope(HttpMethod.Get, "/v1/products?pageSize=100", "products.read");
        using var listResponse = await client.SendAsync(listRequest);
        var envelope = await listResponse.Content.ReadFromJsonAsync<ProductListEnvelope>();
        envelope!.Items.ShouldNotContain(p => p.Name == "Gadget");
    }

    [Fact]
    public async Task TheWriteScope_IsNotEnoughForTheRouteThatKeepsItsOwnScope()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var widget = await CreateProductAsync(client, "Widget", 9.99m);

        using var request = WithScope(
            HttpMethod.Put, $"/v1/products/{widget.Id}/supplier-reference", "products.write",
            new { supplierReferenceCode = "SUP-001" });
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheRoutesOwnScope_GrantsIt()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var widget = await CreateProductAsync(client, "Widget", 9.99m);

        using var request = WithScope(
            HttpMethod.Put, $"/v1/products/{widget.Id}/supplier-reference", "products.supplier",
            new { supplierReferenceCode = "SUP-001" });
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ProductDto>();
        dto!.SupplierReferenceCode.ShouldBe("SUP-001");
    }

    /// <summary>
    /// DRK-1386 §5 Scenario Outline "A kept generated route is unchanged for its callers" — every generated
    /// route that survives this change must still answer at the same path/verb with the same response shape
    /// once its caller holds the scope the route now requires.
    /// </summary>
    [Theory]
    [InlineData("read by id", "products.read")]
    [InlineData("list", "products.read")]
    [InlineData("create", "products.write")]
    [InlineData("price change", "products.write")]
    [InlineData("approval", "products.write")]
    [InlineData("delete", "products.write")]
    [InlineData("supplier reference assignment", "products.supplier")]
    public async Task AKeptGeneratedRoute_IsUnchangedForItsCallers(string operation, string scope)
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var widget = await CreateProductAsync(client, "Widget", 9.99m);

        var (response, expectedStatus) = await CallKeptOperationAsync(client, operation, scope, widget.Id);

        response.StatusCode.ShouldBe(expectedStatus);
    }

    private static async Task<(HttpResponseMessage Response, HttpStatusCode ExpectedStatus)> CallKeptOperationAsync(
        HttpClient client, string operation, string scope, Guid productId) =>
        operation switch
        {
            "read by id" => (
                await client.SendAsync(WithScope(HttpMethod.Get, $"/v1/products/{productId}", scope)),
                HttpStatusCode.OK),
            "list" => (
                await client.SendAsync(WithScope(HttpMethod.Get, "/v1/products", scope)),
                HttpStatusCode.OK),
            "create" => (
                await client.SendAsync(WithScope(
                    HttpMethod.Post, "/v1/products", scope, new { name = "Kept Create", price = 1.00m })),
                HttpStatusCode.Created),
            "price change" => (
                await client.SendAsync(WithScope(
                    HttpMethod.Put, $"/v1/products/{productId}", scope, new { price = 12.50m })),
                HttpStatusCode.OK),
            "approval" => (
                await client.SendAsync(WithScope(
                    HttpMethod.Post, $"/v1/products/{productId}/approval", scope, new { byUser = "alice" })),
                HttpStatusCode.OK),
            // DRK-1410: delete now refuses a product still for sale — discontinue it first (all scopes, so
            // this setup call is never what the scope under test is proving) so the route under test still
            // exercises the "kept route, unchanged for its callers" claim on its success path.
            "delete" => await DeleteAfterDiscontinuingAsync(client, scope, productId),
            "supplier reference assignment" => (
                await client.SendAsync(WithScope(
                    HttpMethod.Put, $"/v1/products/{productId}/supplier-reference", scope,
                    new { supplierReferenceCode = "SUP-KEEP" })),
                HttpStatusCode.OK),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown kept-route operation.")
        };

    private static async Task<(HttpResponseMessage Response, HttpStatusCode ExpectedStatus)> DeleteAfterDiscontinuingAsync(
        HttpClient client, string scope, Guid productId)
    {
        using var discontinueRequest = WithScope(
            HttpMethod.Put, $"/v1/products/{productId}/discontinue", TestAuthHandler.DefaultScopes,
            new { replacementName = "Kept Delete Replacement", replacementPrice = 1.00m });
        using var discontinueResponse = await client.SendAsync(discontinueRequest);
        discontinueResponse.EnsureSuccessStatusCode();

        return (
            await client.SendAsync(WithScope(HttpMethod.Delete, $"/v1/products/{productId}", scope)),
            HttpStatusCode.NoContent);
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
