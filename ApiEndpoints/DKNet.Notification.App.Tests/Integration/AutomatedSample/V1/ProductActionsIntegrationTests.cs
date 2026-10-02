using System.Net.Http.Json;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.AutomatedSample.V1;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// HTTP-level coverage for the hand-written discontinue route's not-found and already-discontinued
/// branches — the frozen DRK-1386 acceptance tests don't reach either directly (the "no replacement
/// named" AT scenario 400s from FluentValidation before the handler runs, and no AT calls discontinue on
/// an unknown id). Runs against plain <see cref="ApiFixture"/> (<c>RequireAuthorization</c> off) — the
/// business rule under test doesn't involve scopes, and this also covers the endpoint's
/// authorization-off branch (R2) that <see cref="ProductDiscontinueReplacementTests"/> never exercises.
/// </summary>
public sealed class ProductActionsIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Discontinue_ReturnsNotFound_WhenProductDoesNotExist()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/v1/products/{Guid.NewGuid()}/discontinue",
            new { replacementName = "Replacement", replacementPrice = 1.00m });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Discontinue_ReturnsBadRequest_WhenAlreadyDiscontinued()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var createResponse = await client.PostAsJsonAsync("/v1/products", new { name = "Widget", price = 9.99m });
        var widget = (await createResponse.Content.ReadFromJsonAsync<ProductDto>())!;

        using var first = await client.PutAsJsonAsync(
            $"/v1/products/{widget.Id}/discontinue",
            new { replacementName = "Widget II", replacementPrice = 12.00m });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var second = await client.PutAsJsonAsync(
            $"/v1/products/{widget.Id}/discontinue",
            new { replacementName = "Widget III", replacementPrice = 14.00m });
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var listResponse = await client.GetAsync("/v1/products?pageSize=100");
        var envelope = await listResponse.Content.ReadFromJsonAsync<ProductListEnvelope>();
        envelope!.Items.ShouldNotContain(p => p.Name == "Widget III");
    }

    [Fact]
    public async Task Discontinue_OnlyAffectsTheTargetedProduct_WhenMoreThanOneExists()
    {
        // Proves SpecGetProduct(byId) actually filters to that one row: targets the SECOND-created
        // product specifically, so a missing/removed filter (which would make the lookup return
        // whichever row comes back unfiltered) shows up as the wrong product changing state either way,
        // not just as "no visible difference" the way targeting the first-created row could.
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var firstCreate = await client.PostAsJsonAsync("/v1/products", new { name = "Widget", price = 9.99m });
        var first = (await firstCreate.Content.ReadFromJsonAsync<ProductDto>())!;
        using var secondCreate = await client.PostAsJsonAsync("/v1/products", new { name = "Gadget", price = 5.00m });
        var second = (await secondCreate.Content.ReadFromJsonAsync<ProductDto>())!;

        using var discontinueResponse = await client.PutAsJsonAsync(
            $"/v1/products/{second.Id}/discontinue",
            new { replacementName = "Gadget II", replacementPrice = 6.00m });
        discontinueResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var firstResponse = await client.GetAsync($"/v1/products/{first.Id}");
        var firstAfter = await firstResponse.Content.ReadFromJsonAsync<ProductDto>();
        firstAfter!.IsDiscontinued.ShouldBeFalse();

        using var secondResponse = await client.GetAsync($"/v1/products/{second.Id}");
        var secondAfter = await secondResponse.Content.ReadFromJsonAsync<ProductDto>();
        secondAfter!.IsDiscontinued.ShouldBeTrue();
    }

    [Fact]
    public async Task Summary_ReportsZeroCountAndZeroAverage_WhenNoProductsExist()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var response = await client.GetAsync("/v1/products/summary");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<ProductPriceSummaryResponse>();
        summary!.ProductCount.ShouldBe(0);
        summary.AveragePrice.ShouldBe(0m);
    }

    private sealed record ProductListEnvelope(List<ProductDto> Items);

    private sealed record ProductPriceSummaryResponse(int ProductCount, decimal AveragePrice);
}

/// <summary>
/// Covers <see cref="DKNet.Notification.App.TestSupport.TestAuthHandler"/>'s <c>X-Test-Scopes</c> header parsing when
/// the header is present but empty — falls back to <see cref="DKNet.Notification.App.TestSupport.TestAuthHandler.DefaultScopes"/>
/// the same as when the header is absent entirely, a branch none of the scope-value AT scenarios reach
/// (they always send a non-empty single scope).
/// </summary>
public sealed class TestAuthHandlerScopesHeaderTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    [Fact]
    public async Task EmptyScopesHeader_FallsBackToDefaultScopes_SameAsHeaderAbsent()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/products")
        {
            Content = JsonContent.Create(new { name = "Widget", price = 9.99m })
        };
        request.Headers.TryAddWithoutValidation("X-Test-Scopes", "");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
