using System.Net.Http.Json;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// Proves DKNet's role-gated <c>[SensitiveData]</c> filtering end to end over real HTTP: a caller holding the
/// declared role receives the value, any other caller — role-less or differently-roled — receives a response
/// the property is simply absent from. Always asserted against the raw JSON payload (<see cref="JsonDocument"/>),
/// never a deserialized DTO — a missing property and a null one are indistinguishable once deserialized. The
/// anonymous-caller scenario lives in <see cref="ProductSensitiveDataAnonymousTests"/> instead of here — see
/// that class's remarks for why it cannot share a test class with this one.
/// </summary>
/// <remarks>
/// <c>Product</c> is <see cref="DKNet.Notification.Domains.Share.IOwnedBy"/>, so DKNet's row-level read filter admits only
/// rows whose <c>OwnedBy</c> matches the caller's ownership key — two callers with different subjects cannot
/// read the same product at all (see <c>ProductOwnershipIsolationTests</c>, a separate axis from this one). The
/// two-caller scenario below therefore shares one <see cref="MultiSubjectAuthHandler.SubjectHeaderName"/> across
/// both callers and varies only <see cref="MultiSubjectAuthHandler.RolesHeaderName"/> — that is the axis under
/// test: one endpoint, one response model, one process, two role sets, two independent verdicts.
/// </remarks>
public sealed class ProductSensitiveDataTests(AuthOnMultiSubjectApiFixture fixture)
    : IClassFixture<AuthOnMultiSubjectApiFixture>
{
    private const string SharedSubject = "pricing-scenario-subject";
    private const string ProductName = "Espresso Machine";
    private const decimal ProductPrice = 899.00m;
    private const decimal SupplierCostPrice = 412.50m;
    private const string SupplierReferenceCode = "SUP-88421";

    [Fact]
    public async Task PricingAnalyst_Receives_SupplierCostPrice()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var productId = await CreateProductAsync(client, SharedSubject, name: ProductName);

        using var json = await GetProductJsonAsync(client, productId, SharedSubject, "pricing");

        json.RootElement.GetProperty("supplierCostPrice").GetDecimal().ShouldBe(SupplierCostPrice);
    }

    [Fact]
    public async Task SupportAgentWithoutPricingRole_ReceivesNoSupplierCostPrice_ButStillReceivesEverythingElse()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var productId = await CreateProductAsync(client, SharedSubject, name: ProductName);

        using var json = await GetProductJsonAsync(client, productId, SharedSubject, "support");

        json.RootElement.TryGetProperty("supplierCostPrice", out _).ShouldBeFalse();
        json.RootElement.GetProperty("name").GetString().ShouldBe(ProductName);
        json.RootElement.GetProperty("price").GetDecimal().ShouldBe(ProductPrice);
        // Names no role — any authenticated caller receives it, "support"-only included.
        json.RootElement.GetProperty("supplierReferenceCode").GetString().ShouldBe(SupplierReferenceCode);
    }

    [Fact]
    public async Task TwoCallersOfSameEndpoint_AreJudgedIndependently_InEitherOrder()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var productId = await CreateProductAsync(client, SharedSubject, name: ProductName);

        // Mia (pricing) then Raj (support).
        using (var mia = await GetProductJsonAsync(client, productId, SharedSubject, "pricing"))
        using (var raj = await GetProductJsonAsync(client, productId, SharedSubject, "support"))
        {
            mia.RootElement.GetProperty("supplierCostPrice").GetDecimal().ShouldBe(SupplierCostPrice);
            raj.RootElement.TryGetProperty("supplierCostPrice", out _).ShouldBeFalse();
        }

        // Same holds in the opposite order — no shared-response-model-metadata cross-contamination.
        using (var raj = await GetProductJsonAsync(client, productId, SharedSubject, "support"))
        using (var mia = await GetProductJsonAsync(client, productId, SharedSubject, "pricing"))
        {
            raj.RootElement.TryGetProperty("supplierCostPrice", out _).ShouldBeFalse();
            mia.RootElement.GetProperty("supplierCostPrice").GetDecimal().ShouldBe(SupplierCostPrice);
        }
    }

    [Fact]
    public async Task AuthenticatedCallerWithNoRolesAtAll_ReceivesReferenceCodeButNotCostPrice()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var productId = await CreateProductAsync(client, SharedSubject, name: ProductName);

        // Deliberately no X-Test-Roles header at all.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/products/{productId}");
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, SharedSubject);
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        json.RootElement.TryGetProperty("supplierCostPrice", out _).ShouldBeFalse();
        json.RootElement.GetProperty("supplierReferenceCode").GetString().ShouldBe(SupplierReferenceCode);
    }

    [Fact]
    public async Task CallerHoldingPricingAlongsideOtherRoles_StillReceivesSupplierCostPrice()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var productId = await CreateProductAsync(client, SharedSubject, name: ProductName);

        using var json = await GetProductJsonAsync(client, productId, SharedSubject, "support,pricing,ops");

        json.RootElement.GetProperty("supplierCostPrice").GetDecimal().ShouldBe(SupplierCostPrice);
    }

    [Fact]
    public async Task EmptyRolesHeader_BehavesTheSameAsNoRolesHeaderAtAll_ReceivesReferenceCodeButNotCostPrice()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var productId = await CreateProductAsync(client, SharedSubject, name: ProductName);

        // X-Test-Roles present but empty — a distinct wire shape from the header being absent entirely.
        using var json = await GetProductJsonAsync(client, productId, SharedSubject, roles: "");

        json.RootElement.TryGetProperty("supplierCostPrice", out _).ShouldBeFalse();
        json.RootElement.GetProperty("supplierReferenceCode").GetString().ShouldBe(SupplierReferenceCode);
    }

    /// <summary>
    /// Creates a product via <c>POST /v1/products</c> (which now only ever accepts the pre-existing
    /// <c>name</c>/<c>price</c> plus the optional <c>supplierCostPrice</c> — a reference is never
    /// caller-settable at create time), then assigns its supplier reference code through the dedicated
    /// <c>PUT /v1/products/{id}/supplier-reference</c> action — a real request, not a DB write, so every
    /// demonstration product actually carries a non-null reference code the R2 assertions can prove against.
    /// </summary>
    private static async Task<Guid> CreateProductAsync(HttpClient client, string subject, string name)
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/products")
        {
            Content = JsonContent.Create(new { name, price = ProductPrice, supplierCostPrice = SupplierCostPrice })
        };
        createRequest.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, subject);

        using var createResponse = await client.SendAsync(createRequest);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var productId = body.RootElement.GetProperty("id").GetGuid();

        using var assignRequest = new HttpRequestMessage(HttpMethod.Put, $"/v1/products/{productId}/supplier-reference")
        {
            Content = JsonContent.Create(new { supplierReferenceCode = SupplierReferenceCode })
        };
        assignRequest.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, subject);
        using var assignResponse = await client.SendAsync(assignRequest);
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        return productId;
    }

    private static async Task<JsonDocument> GetProductJsonAsync(
        HttpClient client, Guid productId, string subject, string roles)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/products/{productId}");
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, subject);
        request.Headers.Add(MultiSubjectAuthHandler.RolesHeaderName, roles);
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
