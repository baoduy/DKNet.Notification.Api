using System.Net.Http.Json;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.AutomatedSample.V1;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// The automated sample's half of the cycle's sharpest acceptance point. Unlike the manual sample, the
/// generated create request has no acting-user property at all to spoof (see
/// <c>Architecture.SampleInvariantTests.GeneratedCreateProductRequest_ShouldCarryNoActingUserProperty</c>) —
/// this test proves the runtime consequence: <c>CreatedBy</c> is stamped from the authenticated caller via
/// <c>DataOwnerHook</c>, regardless of anything the payload contains.
/// </summary>
/// <remarks>
/// <c>DataOwnerHook</c> stamps <c>CreatedBy</c> from <c>IDataOwnerProvider.GetOwnershipKey()</c>, which
/// <c>PrincipalProvider</c> implements as <c>ProfileId.ToString()</c> (read from the <c>NameIdentifier</c>
/// claim) — not from the caller's name. Assert against <see cref="TestAuthHandler.CallerProfileId"/>, not
/// <see cref="TestAuthHandler.CallerName"/>.
/// </remarks>
public sealed class ProductSecurityTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    [Fact]
    public async Task Create_ShouldStampCreatedByFromAuthenticatedCallersOwnershipKey()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/products", new { name = "Widget", price = 9.99m });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<ProductDto>();
        dto!.CreatedBy.ShouldBe(TestAuthHandler.CallerProfileId.ToString());
    }

    [Fact]
    public async Task Create_ShouldIgnoreAnyExtraActingUserFieldInThePayload()
    {
        // Even a caller who tries to smuggle an acting-user value onto an unknown JSON property gets nothing —
        // System.Text.Json ignores properties the target type doesn't declare, and CreateProductRequest
        // (generated) declares none.
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/products", new
        {
            name = "Gadget",
            price = 5.00m,
            createdBy = "someone-else",
            byUser = "someone-else"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<ProductDto>();
        dto!.CreatedBy.ShouldBe(TestAuthHandler.CallerProfileId.ToString());
        dto.CreatedBy.ShouldNotBe("someone-else");
    }

    [Fact]
    public async Task Update_ShouldStampUpdatedByFromAuthenticatedCallersOwnershipKey()
    {
        // DKNet 10.1.12 closed DataOwnerHook's UpdatedBy/UpdatedOn gap (DRK-735) — this was previously a
        // known-and-accepted limitation this cycle explicitly excused; now it's a real, testable guarantee.
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        var created = await (await client.PostAsJsonAsync("/v1/products", new { name = "Widget", price = 9.99m }))
            .Content.ReadFromJsonAsync<ProductDto>();

        var updateResponse = await client.PutAsJsonAsync(
            $"/v1/products/{created!.Id}",
            new { price = 12.50m, updatedBy = "someone-else" });

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await updateResponse.Content.ReadFromJsonAsync<ProductDto>();
        dto!.Price.ShouldBe(12.50m);
        dto.UpdatedBy.ShouldBe(TestAuthHandler.CallerProfileId.ToString());
        dto.UpdatedBy.ShouldNotBe("someone-else");
    }

    /// <remarks>
    /// Unlike create/update, <c>approval</c> is a <c>[CrudAction]</c> route: <see cref="Product.Approve"/>
    /// calls <c>SetUpdatedBy(byUser)</c> directly, which writes <c>UpdatedBy</c>/<c>UpdatedOn</c> together —
    /// <c>DataOwnerHook</c> treats that as an explicit modifier already supplied for the change set and
    /// leaves it untouched (see <c>DataOwnerHook.HasExplicitModifier</c>). So, deliberately and unlike
    /// create/update, the payload's acting user wins here — the standing guarantee documented in
    /// <c>docs/samples/automated-products/README.md</c>.
    /// </remarks>
    [Fact]
    public async Task Approve_ShouldStampUpdatedByFromThePayloadsActingUser()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        var created = await (await client.PostAsJsonAsync("/v1/products", new { name = "Widget", price = 9.99m }))
            .Content.ReadFromJsonAsync<ProductDto>();

        var approveResponse = await client.PostAsJsonAsync(
            $"/v1/products/{created!.Id}/approval",
            new { byUser = "someone-else" });

        approveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await approveResponse.Content.ReadFromJsonAsync<ProductDto>();
        dto!.UpdatedBy.ShouldBe("someone-else");
    }
}
