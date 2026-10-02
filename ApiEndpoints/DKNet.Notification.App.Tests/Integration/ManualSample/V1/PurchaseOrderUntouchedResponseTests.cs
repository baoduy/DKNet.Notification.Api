using System.Net.Http.Json;
using DKNet.Notification.App.Tests.Integration.Support;

namespace DKNet.Notification.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// The untouched-response witness for the sample API's role-aware JSON opt-in (DRK-1188): <c>PurchaseOrder</c>
/// declares nothing <c>[SensitiveData]</c>, so an opted-in host must serve it exactly as before — for every
/// caller, including anonymous. Asserted against the raw JSON payload, same discipline as
/// <c>ProductSensitiveDataTests</c>.
/// </summary>
public sealed class PurchaseOrderUntouchedResponseTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OptedInHost_StillReturnsFullPurchaseOrderResponse_ForAnUnauthenticatedCaller()
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new { customerName = "Acme Pte Ltd", amount = 1250.00m })
        };
        createRequest.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        using var createResponse = await client.SendAsync(createRequest);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();

        using var getResponse = await client.GetAsync($"/v1/purchase-orders/{id}");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());

        json.RootElement.GetProperty("customerName").GetString().ShouldBe("Acme Pte Ltd");
        json.RootElement.GetProperty("amount").GetDecimal().ShouldBe(1250.00m);
    }
}
