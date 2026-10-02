using System.Net.Http.Json;
using DKNet.Notification.AppServices.ManualSample.V1;

namespace DKNet.Notification.App.BDDTests.Features.PurchaseOrders.Steps;

[Binding]
public sealed class PurchaseOrderSteps(HttpClient client, ScenarioState state, BddApiFactory factory)
{
    private Guid _lastId;

    // DRK-1410: purchase orders created for the cross-type "delete rule refuses nothing else" scenario,
    // keyed by the business-readable key the Gherkin names them by (e.g. "PO-1001").
    private readonly Dictionary<string, Guid> _ordersByKey = new();

    public Guid GetIdByKey(string key) => _ordersByKey[key];

    #region When

    [When(@"catalogue-ops lists purchase orders with page index (-?\d+)")]
    public async Task WhenCatalogueOpsListsPurchaseOrdersWithPageIndex(int pageIndex)
    {
        state.Response = await client.GetAsync($"/v1/purchase-orders?pageIndex={pageIndex}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I create a purchase order for customer ""(.*)"" with amount (.*)")]
    public Task WhenICreateAPurchaseOrder(string customerName, decimal amount) =>
        CreateAsync(customerName, amount, Guid.NewGuid().ToString());

    [When(@"I create a purchase order for customer ""(.*)"" with amount (.*) using idempotency key ""(.*)""")]
    public Task WhenICreateAPurchaseOrderUsingIdempotencyKey(string customerName, decimal amount, string key) =>
        CreateAsync(customerName, amount, key);

    [When(@"I create a purchase order for customer ""(.*)"" with amount (.*) without an idempotency key")]
    public async Task WhenICreateAPurchaseOrderWithoutAnIdempotencyKey(string customerName, decimal amount)
    {
        state.Response = await client.PostAsJsonAsync("/v1/purchase-orders", new { customerName, amount });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I replay the same create request with idempotency key ""(.*)""")]
    public async Task WhenIReplayTheSameCreateRequest(string key)
    {
        var previousId = _lastId;
        await CreateAsync("Acme Pte Ltd", 250.00m, key);
        state.ResponseBody = JsonSerializer.Serialize(new { previousId, replayedId = _lastId });
    }

    [When(@"I get the purchase order with id ""(.*)""")]
    public async Task WhenIGetThePurchaseOrderWithId(Guid id)
    {
        state.Response = await client.GetAsync($"/v1/purchase-orders/{id}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I get that purchase order")]
    public async Task WhenIGetThatPurchaseOrder()
    {
        state.Response = await client.GetAsync($"/v1/purchase-orders/{_lastId}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I list purchase orders")]
    public async Task WhenIListPurchaseOrders()
    {
        state.Response = await client.GetAsync("/v1/purchase-orders?PageIndex=1&PageSize=50");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I update that purchase order's amount to (.*)")]
    public async Task WhenIUpdateThatPurchaseOrdersAmountTo(decimal amount)
    {
        state.Response = await client.PutAsJsonAsync($"/v1/purchase-orders/{_lastId}", new { amount });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I cancel that purchase order")]
    [When("I cancel that purchase order again")]
    public async Task WhenICancelThatPurchaseOrder()
    {
        state.Response = await client.PostAsync($"/v1/purchase-orders/{_lastId}/cancel", null);
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I delete that purchase order")]
    public async Task WhenIDeleteThatPurchaseOrder()
    {
        state.Response = await client.DeleteAsync($"/v1/purchase-orders/{_lastId}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    #endregion

    #region Given

    [Given(@"a purchase order exists for customer ""(.*)"" with amount (.*)")]
    public async Task GivenAPurchaseOrderExists(string customerName, decimal amount)
    {
        await CreateAsync(customerName, amount, Guid.NewGuid().ToString());
        state.Response!.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Given(@"the purchase order ""(.*)"" for ""(.*)"" exists")]
    public async Task GivenThePurchaseOrderForExists(string key, string customerName)
    {
        await CreateAsync(customerName, 100.00m, Guid.NewGuid().ToString());
        state.Response!.IsSuccessStatusCode.ShouldBeTrue();
        _ordersByKey[key] = _lastId;
    }

    #endregion

    #region Then

    [Then(@"the purchase order response has customer name ""(.*)"" and amount (.*)")]
    public void ThenThePurchaseOrderResponseHasCustomerNameAndAmount(string customerName, decimal amount)
    {
        var dto = Deserialize();
        dto.CustomerName.ShouldBe(customerName);
        dto.Amount.ShouldBe(amount);
    }

    [Then(@"the purchase order response has amount (.*)")]
    public void ThenThePurchaseOrderResponseHasAmount(decimal amount) => Deserialize().Amount.ShouldBe(amount);

    [Then(@"the purchase order response status is ""(.*)""")]
    public void ThenThePurchaseOrderResponseStatusIs(string status)
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        state.ResponseBody!.ShouldContain($"\"status\":\"{status}\"");
    }

    [Then("both responses report the same purchase order id")]
    public void ThenBothResponsesReportTheSamePurchaseOrderId()
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        doc.RootElement.GetProperty("previousId").GetGuid()
            .ShouldBe(doc.RootElement.GetProperty("replayedId").GetGuid());
    }

    [Then(@"the response includes a purchase order for customer ""(.*)""")]
    public void ThenTheResponseIncludesAPurchaseOrderForCustomer(string customerName)
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        var list = JsonSerializer.Deserialize<List<PurchaseOrderDto>>(state.ResponseBody!, SharedConsts.JsonSerializerOptions);
        list.ShouldNotBeNull();
        list!.ShouldContain(o => o.CustomerName == customerName);
    }

    [Then("a log line reports the purchase order created event was received")]
    public void ThenALogLineReportsThePurchaseOrderCreatedEventWasReceived() =>
        factory.LogCapture.Messages.ShouldContain(m => m.Contains("PurchaseOrderCreatedEvent received", StringComparison.Ordinal));

    #endregion

    private async Task CreateAsync(string customerName, decimal amount, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/purchase-orders")
        {
            Content = JsonContent.Create(new { customerName, amount })
        };
        request.Headers.Add("X-Idempotency-Key", idempotencyKey);

        state.Response = await client.SendAsync(request);
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();

        if (state.Response.IsSuccessStatusCode)
        {
            var dto = JsonSerializer.Deserialize<PurchaseOrderDto>(state.ResponseBody, SharedConsts.JsonSerializerOptions);
            _lastId = dto!.Id;
        }
    }

    private PurchaseOrderDto Deserialize()
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        return JsonSerializer.Deserialize<PurchaseOrderDto>(state.ResponseBody!, SharedConsts.JsonSerializerOptions)!;
    }
}
