using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.AutomatedSample.V1;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;
using DKNet.Notification.Infra.Contexts;

namespace DKNet.Notification.App.Tests.Integration.AutomatedSample.V1;

/// <summary>
/// DRK-1466 §5 — the acting user (<c>CreatedBy</c>/<c>UpdatedBy</c>) and the tenant-ownership key
/// (<c>OwnedBy</c>) must come from two independent sources. <see cref="AuthOnFixedTenantApiFixture" /> pins
/// the tenant key to one constant while letting the signed-in subject vary, so a collapse back onto one
/// shared claim shows up here as a wrong <c>CreatedBy</c>/<c>UpdatedBy</c>, not merely as an equal string
/// (mirrors <see cref="ProductOwnershipIsolationTests" />'s subject/tenant split for the read-filter side).
/// </summary>
public sealed class ProductAuditStampTests(AuthOnFixedTenantApiFixture fixture)
    : IClassFixture<AuthOnFixedTenantApiFixture>
{
    private const string ActingSubject = "9f2c1b44-0a7e-4c3d-8b21-77d9e5a10c62";

    [Fact]
    public async Task Create_RecordsTheActingSubjectAsCreator_AndTheFixedTenantAsOwner()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/products")
        {
            Content = JsonContent.Create(new { name = "Blue Widget", price = 9.99m })
        };
        request.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, ActingSubject);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<ProductDto>();
        dto!.CreatedBy.ShouldBe(ActingSubject);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var stored = await dbContext.Set<Product>().IgnoreQueryFilters().SingleAsync(p => p.Id == dto.Id);
        stored.OwnedBy.ShouldBe(AuthOnFixedTenantApiFixture.TenantKey);
    }

    [Fact]
    public async Task Update_RecordsTheActingSubjectAsLastEditor_AndLeavesTheOwnerUnchanged()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/products")
        {
            Content = JsonContent.Create(new { name = "Blue Widget", price = 9.99m })
        };
        createRequest.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, ActingSubject);
        using var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>();

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/v1/products/{created!.Id}")
        {
            Content = JsonContent.Create(new { price = 24.50m })
        };
        updateRequest.Headers.Add(MultiSubjectAuthHandler.SubjectHeaderName, ActingSubject);

        using var updateResponse = await client.SendAsync(updateRequest);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductDto>();
        updated!.UpdatedBy.ShouldBe(ActingSubject);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var stored = await dbContext.Set<Product>().IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id);
        stored.OwnedBy.ShouldBe(AuthOnFixedTenantApiFixture.TenantKey);
    }
}
