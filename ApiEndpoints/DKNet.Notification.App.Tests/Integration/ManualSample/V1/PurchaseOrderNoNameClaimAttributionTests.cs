using System.Net.Http.Json;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// Proves the no-name-claim-while-authenticated refusal over HTTP for the three writes
/// <c>PurchaseOrderStampingAndVersioningTests</c> does not cover (it owns Create). Update, Cancel and Delete
/// each carry their own <c>[FromClaim]</c>-declared <c>ByUser</c> and their own
/// <c>string.IsNullOrEmpty(ByUser)</c> guard, and Cancel/Delete bind via <c>[AsParameters]</c> rather than a
/// JSON body — a create-only test would not prove population reaches either shape.
/// </summary>
public sealed class PurchaseOrderNoNameClaimAttributionTests(AuthOnNoNameClaimApiFixture fixture)
    : IClassFixture<AuthOnNoNameClaimApiFixture>
{
    [Fact]
    public async Task Update_WithNoNameClaim_IsRefused_AndAmountIsUnchanged()
    {
        await fixture.ResetDatabaseAsync();
        var order = await SeedOrderAsync();
        var client = fixture.CreateClient();

        using var response = await client.PutAsJsonAsync($"/v1/purchase-orders/{order.Id}", new { amount = 999m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var reloaded = await FindAsync(order.Id);
        reloaded!.Amount.ShouldBe(order.Amount);
    }

    [Fact]
    public async Task Cancel_WithNoNameClaim_IsRefused_AndStatusIsUnchanged()
    {
        await fixture.ResetDatabaseAsync();
        var order = await SeedOrderAsync();
        var client = fixture.CreateClient();

        using var response = await client.PostAsync($"/v1/purchase-orders/{order.Id}/cancel", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var reloaded = await FindAsync(order.Id);
        reloaded!.Status.ShouldBe(PurchaseOrderStatus.Placed);
    }

    [Fact]
    public async Task Delete_WithNoNameClaim_IsRefused_AndOrderStillExists()
    {
        await fixture.ResetDatabaseAsync();
        var order = await SeedOrderAsync();
        var client = fixture.CreateClient();

        using var response = await client.DeleteAsync($"/v1/purchase-orders/{order.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var reloaded = await FindAsync(order.Id);
        reloaded.ShouldNotBeNull();
    }

    private async Task<PurchaseOrder> SeedOrderAsync()
    {
        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();
        var order = new PurchaseOrder("Acme Pte Ltd", 100m, "seed");
        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        return order;
    }

    private async Task<PurchaseOrder?> FindAsync(Guid id)
    {
        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();
        return await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(id), CancellationToken.None);
    }
}
