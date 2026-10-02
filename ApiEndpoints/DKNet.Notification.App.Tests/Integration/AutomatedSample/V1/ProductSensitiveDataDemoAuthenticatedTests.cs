using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;
using DKNet.Notification.Infra.Contexts;
using DKNet.Notification.Share;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// The mirror image of <see cref="ProductSensitiveDataAnonymousTests"/>: on a bare <see cref="ApiFixture"/>,
/// the built-in demonstration authentication provider is on by default (DRK-1579), so the caller is
/// authenticated — a role-less <c>[SensitiveData]</c> property is visible to any authenticated caller
/// (<c>Product.cs:80-86</c>), demo identity included. Proves the demonstration scheme's authentication
/// actually reaches <c>HttpContext.User</c> on a read route that never goes through <c>[FromClaim]</c>
/// population, distinct from <see cref="DKNet.Notification.App.Tests.Integration.EndpointConfig.PurchaseOrderStampingAndVersioningTests"/>'s
/// coverage of the write path.
/// </summary>
public sealed class ProductSensitiveDataDemoAuthenticatedTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string ProductName = "Espresso Machine";
    private const decimal ProductPrice = 899.00m;
    private const decimal SupplierCostPrice = 412.50m;
    private const string SupplierReferenceCode = "SUP-88421";

    [Fact]
    public async Task DemoAuthenticatedCaller_ReceivesTheRoleLessSensitiveProperty_ButNotThePricingRoleOne()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();
        var productId = await SeedProductDirectlyAsync(fixture, ProductName);

        var response = await client.GetAsync($"/v1/products/{productId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        json.RootElement.TryGetProperty("supplierCostPrice", out _).ShouldBeFalse();
        json.RootElement.GetProperty("supplierReferenceCode").GetString().ShouldBe(SupplierReferenceCode);
    }

    /// <summary>
    /// Same seeding shape as <see cref="ProductSensitiveDataAnonymousTests.SeedProductDirectlyAsync"/> —
    /// see its remarks for why the write goes straight through <see cref="CoreDbContext"/>.
    /// </summary>
    private static async Task<Guid> SeedProductDirectlyAsync(ApiFixture apiFixture, string name)
    {
        using var scope = apiFixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var product = new Product(name, ProductPrice, SupplierCostPrice);
        product.AssignSupplierReference(SupplierReferenceCode);
        dbContext.Add(product);
        var entry = dbContext.Entry(product);
        entry.Property(nameof(Product.CreatedBy)).CurrentValue = SharedConsts.SystemAccount;
        entry.Property(nameof(Product.CreatedOn)).CurrentValue = DateTimeOffset.UtcNow;
        entry.Property(nameof(Product.OwnedBy)).CurrentValue = SharedConsts.SystemAccount;

        await dbContext.SaveChangesAsync();
        return product.Id;
    }
}
