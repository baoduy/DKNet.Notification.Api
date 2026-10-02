using System.Net.Http.Json;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// Acceptance tests for DRK-1430 §5: <c>ProductDto.GrossMargin</c> (<c>Price - SupplierCostPrice</c>), a
/// hand-mapped value the generator's name-matching convention cannot produce. Frozen at <c>at_sha</c>
/// once approved — see the sub-task brief for the change set these prove.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ProductSensitiveDataTests"/>: raw <see cref="JsonDocument"/> assertions against
/// <see cref="AuthOnMultiSubjectApiFixture"/>, one shared subject, roles varied per request via
/// <see cref="MultiSubjectAuthHandler.RolesHeaderName"/>. <c>MultiSubjectAuthHandler</c> grants every
/// <c>ProductScopes</c> entry unconditionally, so the "pricing" role header controls only the
/// <c>[SensitiveData]</c> visibility axis under test here, never whether a route is reachable.
/// </remarks>
public sealed class ProductGrossMarginTests(AuthOnMultiSubjectApiFixture fixture)
    : IClassFixture<AuthOnMultiSubjectApiFixture>
{
    private const string Subject = "gross-margin-scenario-subject";
    private const string PricingRole = "pricing";

    #region Scenario Outline: Every generated product route reports the computed gross margin

    [Fact]
    public async Task ReadById_ReportsComputedGrossMargin()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var id = await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var json = await GetJsonAsync(client, HttpMethod.Get, $"/v1/products/{id}");

        json.RootElement.GetProperty("grossMargin").GetDecimal().ShouldBe(60.00m);
    }

    [Fact]
    public async Task ListProducts_ReportsComputedGrossMarginForEachItem()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var json = await GetJsonAsync(client, HttpMethod.Get, "/v1/products");

        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == "Steel Widget");
        item.GetProperty("grossMargin").GetDecimal().ShouldBe(60.00m);
    }

    [Fact]
    public async Task CreateProduct_ReportsComputedGrossMarginOnTheResponseOfTheWrite()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var json = await CreateProductJsonAsync(client, "Brass Hinge", 40.00m, 25.00m);

        json.RootElement.GetProperty("grossMargin").GetDecimal().ShouldBe(15.00m);
    }

    [Fact]
    public async Task ChangePrice_RecomputesGrossMarginOnTheResponseOfTheWrite()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var id = await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var json = await GetJsonAsync(client, HttpMethod.Put, $"/v1/products/{id}",
            new { price = 200.00m });

        json.RootElement.GetProperty("grossMargin").GetDecimal().ShouldBe(110.00m);
    }

    [Fact]
    public async Task Approve_ReportsComputedGrossMarginOnTheResponseOfTheWrite()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var id = await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var json = await GetJsonAsync(client, HttpMethod.Post, $"/v1/products/{id}/approval",
            new { byUser = "alice" });

        json.RootElement.GetProperty("grossMargin").GetDecimal().ShouldBe(60.00m);
    }

    [Fact]
    public async Task AssignSupplierReference_ReportsComputedGrossMarginOnTheResponseOfTheWrite()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var id = await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var json = await GetJsonAsync(client, HttpMethod.Put, $"/v1/products/{id}/supplier-reference",
            new { supplierReferenceCode = "SUP-7788" });

        json.RootElement.GetProperty("grossMargin").GetDecimal().ShouldBe(60.00m);
    }

    #endregion

    #region Scenario: Gross margin is empty when no supplier cost price was disclosed

    [Fact]
    public async Task GrossMargin_IsNull_WhenSupplierCostPriceWasNeverDisclosed()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var id = await CreateProductAsync(client, "Paper Clip", 2.00m, supplierCostPrice: null);

        using var json = await GetJsonAsync(client, HttpMethod.Get, $"/v1/products/{id}");

        // A pricing-role caller keeps the key even when its source is undisclosed — role-aware
        // serialization gates the property on role membership, not on nullness (proven empirically here:
        // supplierCostPrice itself is present-with-null for this same caller, never omitted for null
        // alone). R1 forbids a zero/default standing in for "undisclosed" — it must be a literal JSON null.
        json.RootElement.GetProperty("supplierCostPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("grossMargin").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("price").GetDecimal().ShouldBe(2.00m);
    }

    #endregion

    #region Scenario: A caller without the pricing role never sees the derived value

    [Fact]
    public async Task CallerWithoutPricingRole_ReceivesNeitherSupplierCostPriceNorGrossMargin()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var id = await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var json = await GetJsonAsync(client, HttpMethod.Get, $"/v1/products/{id}", roles: "support");

        json.RootElement.TryGetProperty("supplierCostPrice", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("grossMargin", out _).ShouldBeFalse();
        json.RootElement.GetProperty("name").GetString().ShouldBe("Steel Widget");
        json.RootElement.GetProperty("price").GetDecimal().ShouldBe(150.00m);
    }

    #endregion

    #region Scenario Outline: The list route's query surface is unchanged by the customisation

    [Fact]
    public async Task ListFilteredByPrice_StillFilters_AndReportsGrossMarginOnTheSurvivor()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);
        await CreateProductAsync(client, "Paper Clip", 2.00m, supplierCostPrice: null);

        using var json = await GetJsonAsync(client, HttpMethod.Get,
            "/v1/products?filter=price:GreaterThan:100");

        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(1);
        items[0].GetProperty("name").GetString().ShouldBe("Steel Widget");
        items[0].GetProperty("grossMargin").GetDecimal().ShouldBe(60.00m);
    }

    [Fact]
    public async Task ListSearchedByText_StillSearches_UnaffectedByTheNewField()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);
        await CreateProductAsync(client, "Paper Clip", 2.00m, supplierCostPrice: null);

        using var json = await GetJsonAsync(client, HttpMethod.Get, "/v1/products?search=Clip");

        var names = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        names.ShouldBe(["Paper Clip"]);
    }

    [Fact]
    public async Task ListSortedByGrossMargin_IsRefusedAsAClientError_NeverAServerError()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var response = await SendAsync(client, HttpMethod.Get, "/v1/products?orderBy=grossMargin");

        // R3/R5: a computed DTO field with no entity counterpart must 400, never 500 — measured against
        // DKNet.AspCore.Extensions 10.1.29's ListQuery.TryValidate, which checks Declares<TEntity> too.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Contains("grossMargin", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Fact]
    public async Task ListFilteredByGrossMargin_IsRefusedAsAClientError_NeverAServerError()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        await CreateProductAsync(client, "Steel Widget", 150.00m, 90.00m);

        using var response = await SendAsync(client, HttpMethod.Get,
            "/v1/products?filter=grossMargin:GreaterThan:0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Contains("grossMargin", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    #endregion

    #region Scenario: The customisation adds to the convention instead of replacing it

    [Fact]
    public async Task ProductResponse_CarriesConventionValuesUnchanged_AlongsideTheHandMappedGrossMargin()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var json = await CreateProductJsonAsync(client, "Steel Widget", 150.00m, 90.00m);

        // Values the name-matching convention still produces, untouched by the customisation (R6/change-set
        // row 2: ForType(...).Map(...) merges onto the convention config; NewConfig would have replaced it).
        json.RootElement.GetProperty("name").GetString().ShouldBe("Steel Widget");
        json.RootElement.GetProperty("price").GetDecimal().ShouldBe(150.00m);
        json.RootElement.TryGetProperty("createdOn", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("createdBy", out _).ShouldBeTrue();
        // The one hand-mapped value, present alongside the convention-produced ones above.
        json.RootElement.GetProperty("grossMargin").GetDecimal().ShouldBe(60.00m);
    }

    #endregion

    private static async Task<Guid> CreateProductAsync(
        HttpClient client, string name, decimal price, decimal? supplierCostPrice)
    {
        using var json = await CreateProductJsonAsync(client, name, price, supplierCostPrice);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> CreateProductJsonAsync(
        HttpClient client, string name, decimal price, decimal? supplierCostPrice)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/products")
        {
            Content = JsonContent.Create(new { name, price, supplierCostPrice })
        };
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, Subject);
        request.Headers.Add(MultiSubjectAuthHandler.RolesHeaderName, PricingRole);

        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body = null, string roles = PricingRole)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, Subject);
        request.Headers.Add(MultiSubjectAuthHandler.RolesHeaderName, roles);
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient client, HttpMethod method, string url, object? body = null, string roles = PricingRole)
    {
        var response = await SendAsync(client, method, url, body, roles);
        (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created).ShouldBeTrue(
            $"expected OK or Created, got {response.StatusCode}");
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
