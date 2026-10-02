using System.Net.Http.Json;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.ManualSample.V1;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;
using DKNet.Notification.Share;

namespace DKNet.Notification.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// The manual sample's half of the cycle's sharpest acceptance point: a write whose payload claims
/// <c>"byUser": "someone-else"</c> must be attributed to the authenticated caller, not the payload value.
/// Exercised over real HTTP against <see cref="AuthOnApiFixture"/> so the model-binding pipeline
/// (<c>[FromClaim]</c> + <c>AddContextualRequestPopulation</c>) runs for real — contextual population is the
/// single mechanism, and <c>ContextualRequestPopulationService.Populate</c> sets <c>ByUser</c> unconditionally,
/// so a caller-supplied value in the body or query string is always discarded, never trusted.
/// </summary>
public sealed class PurchaseOrderSecurityTests(AuthOnApiFixture fixture) : IClassFixture<AuthOnApiFixture>
{
    [Fact]
    public async Task Create_ShouldAttributeCreatedByToAuthenticatedCaller_IgnoringPayloadByUser()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new
            {
                byUser = "someone-else",
                customerName = "Acme Pte Ltd",
                amount = 100m
            })
        };
        request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<PurchaseOrderDto>(SharedConsts.JsonSerializerOptions);
        dto!.CreatedBy.ShouldBe(TestAuthHandler.CallerName);
        dto.CreatedBy.ShouldNotBe("someone-else");
    }

    [Fact]
    public async Task Update_ShouldAttributeUpdatedByToAuthenticatedCaller_IgnoringPayloadByUser()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new { customerName = "Acme Pte Ltd", amount = 100m })
        };
        createRequest.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        var created = await (await client.SendAsync(createRequest)).Content
            .ReadFromJsonAsync<PurchaseOrderDto>(SharedConsts.JsonSerializerOptions);

        var updateResponse = await client.PutAsJsonAsync($"/v1/purchase-orders/{created!.Id}", new
        {
            byUser = "someone-else",
            amount = 500m
        });

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await updateResponse.Content.ReadFromJsonAsync<PurchaseOrderDto>(SharedConsts.JsonSerializerOptions);
        dto!.Amount.ShouldBe(500m);

        // PurchaseOrder stamps UpdatedBy itself (PurchaseOrder.ChangeAmount -> SetUpdatedBy) — independent of
        // DataOwnerHook either way (DKNet 10.1.12 also closed that hook's own UpdatedBy gap, see
        // ProductSecurityTests.Update_ShouldStampUpdatedByFromAuthenticatedCallersOwnershipKey). PurchaseOrderDto
        // has no UpdatedBy field, so assert it on the entity directly.
        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();
        var order = await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(created.Id), CancellationToken.None);
        order!.UpdatedBy.ShouldBe(TestAuthHandler.CallerName);
        order.UpdatedBy.ShouldNotBe("someone-else");
    }

    [Fact]
    public async Task Cancel_ShouldAttributeUpdatedByToAuthenticatedCaller_IgnoringQueryStringByUser()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new { customerName = "Acme Pte Ltd", amount = 100m })
        };
        createRequest.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        var created = await (await client.SendAsync(createRequest)).Content
            .ReadFromJsonAsync<PurchaseOrderDto>(SharedConsts.JsonSerializerOptions);

        // CancelPurchaseOrderRequest binds via [AsParameters] (PurchaseOrderV1Endpoint.cs:76), which makes
        // ByUser bindable from the query string too — this is the composition the body-only assertions above
        // don't cover.
        var cancelResponse = await client.PostAsync($"/v1/purchase-orders/{created!.Id}/cancel?byUser=someone-else", null);

        cancelResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // PurchaseOrderDto has no UpdatedBy field, so assert it on the entity directly (see the Update test above).
        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();
        var order = await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(created.Id), CancellationToken.None);
        order!.UpdatedBy.ShouldBe(TestAuthHandler.CallerName);
        order.UpdatedBy.ShouldNotBe("someone-else");
    }
}
