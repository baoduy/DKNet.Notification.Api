using System.Linq;
using System.Net.Http.Json;
using DKNet.Notification.App.BDDTests.Features.PurchaseOrders.Steps;
using DKNet.Notification.AppServices.AutomatedSample.V1;

namespace DKNet.Notification.App.BDDTests.Features.Products.Steps;

[Binding]
public sealed class ProductSteps(HttpClient client, ScenarioState state, BddApiFactory factory, PurchaseOrderSteps purchaseOrderSteps)
{
    private Guid _lastId;

    // DRK-1410: products created by name for the precondition scenarios, keyed by the name the Gherkin
    // names them by — separate from _lastId, which only tracks the most recently created product.
    private readonly Dictionary<string, Guid> _productIdsByName = new();

    #region When

    [When(@"I create a product named ""(.*)"" with price (.*)")]
    public Task WhenICreateAProduct(string name, decimal price) => CreateAsync(name, price);

    [When(@"I get the product with id ""(.*)""")]
    public async Task WhenIGetTheProductWithId(Guid id)
    {
        state.Response = await client.GetAsync($"/v1/products/{id}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I get that product")]
    public async Task WhenIGetThatProduct()
    {
        state.Response = await client.GetAsync($"/v1/products/{_lastId}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I list products")]
    public async Task WhenIListProducts()
    {
        state.Response = await client.GetAsync("/v1/products");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I change that product's price to (.*)")]
    public async Task WhenIChangeThatProductsPriceTo(decimal price)
    {
        state.Response = await client.PutAsJsonAsync($"/v1/products/{_lastId}", new { price });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When("I delete that product")]
    public async Task WhenIDeleteThatProduct()
    {
        state.Response = await client.DeleteAsync($"/v1/products/{_lastId}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I approve that product as ""(.*)""")]
    public async Task WhenIApproveThatProductAs(string byUser)
    {
        state.Response = await client.PostAsJsonAsync($"/v1/products/{_lastId}/approval", new { byUser });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I discontinue that product and name ""(.*)"" priced (.*) as its replacement")]
    public async Task WhenIDiscontinueThatProductAndNameReplacementPriced(string replacementName, decimal replacementPrice)
    {
        state.Response = await client.PutAsJsonAsync(
            $"/v1/products/{_lastId}/discontinue",
            new { replacementName, replacementPrice });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    // DRK-1410: catalogue-ops steps below drive the precondition scenarios by product name rather than
    // "that product" (_lastId) — a scenario can juggle more than one named product at once.

    [When(@"catalogue-ops creates the product ""(.*)"" priced (.*) SGD")]
    public Task WhenCatalogueOpsCreatesTheProduct(string name, decimal price) => CreateNamedAsync(name, price);

    [When(@"catalogue-ops deletes ""(.*)""")]
    public async Task WhenCatalogueOpsDeletes(string name)
    {
        state.Response = await client.DeleteAsync($"/v1/products/{_productIdsByName[name]}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"catalogue-ops discontinues ""(.*)"" and names ""(.*)"" priced (.*) SGD as its replacement")]
    public async Task WhenCatalogueOpsDiscontinuesAndNames(string name, string replacementName, decimal replacementPrice)
    {
        state.Response = await client.PutAsJsonAsync(
            $"/v1/products/{_productIdsByName[name]}/discontinue",
            new { replacementName, replacementPrice });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"catalogue-ops calls (.*)")]
    public async Task WhenCatalogueOpsCalls(string operation)
    {
        if (operation.StartsWith("deleting the purchase order ", StringComparison.Ordinal))
        {
            var key = operation["deleting the purchase order ".Length..].Trim();
            state.Response = await client.DeleteAsync($"/v1/purchase-orders/{purchaseOrderSteps.GetIdByKey(key)}");
        }
        else if (operation.Contains("by id", StringComparison.Ordinal))
        {
            state.Response = await client.GetAsync($"/v1/products/{_productIdsByName[ExtractQuoted(operation)]}");
        }
        else if (operation.StartsWith("approving ", StringComparison.Ordinal))
        {
            state.Response = await client.PostAsJsonAsync(
                $"/v1/products/{_productIdsByName[ExtractQuoted(operation)]}/approval",
                new { byUser = "catalogue-ops" });
        }
        else
        {
            throw new NotSupportedException($"Unrecognised operation: {operation}");
        }

        state.ResponseBody = await state.Response!.Content.ReadAsStringAsync();
    }

    #endregion

    #region Given

    [Given(@"a product exists named ""(.*)"" with price (.*)")]
    public async Task GivenAProductExists(string name, decimal price)
    {
        await CreateAsync(name, price);
        state.Response!.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Given(@"the product ""(.*)"" priced (.*) SGD exists")]
    public Task GivenTheProductPricedExists(string name, decimal price) => CreateNamedAsync(name, price);

    [Given(@"the product ""(.*)"" priced (.*) SGD is for sale")]
    public Task GivenTheProductPricedIsForSale(string name, decimal price) => CreateNamedAsync(name, price);

    [Given(@"the product ""(.*)"" priced (.*) SGD is discontinued")]
    public async Task GivenTheProductPricedIsDiscontinued(string name, decimal price)
    {
        await CreateNamedAsync(name, price);
        state.Response = await client.PutAsJsonAsync(
            $"/v1/products/{_productIdsByName[name]}/discontinue",
            new { replacementName = $"{name} (auto-replacement)", replacementPrice = 1.00m });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
        state.Response.IsSuccessStatusCode.ShouldBeTrue();
    }

    #endregion

    #region Then

    [Then(@"the product response has name ""(.*)"" and price (.*)")]
    public void ThenTheProductResponseHasNameAndPrice(string name, decimal price)
    {
        var dto = Deserialize();
        dto.Name.ShouldBe(name);
        dto.Price.ShouldBe(price);
    }

    [Then(@"the product response has price (.*)")]
    public void ThenTheProductResponseHasPrice(decimal price) => Deserialize().Price.ShouldBe(price);

    [Then(@"the response includes a product named ""(.*)""")]
    public void ThenTheResponseIncludesAProductNamed(string name)
    {
        // Unlike ManualSample's hand-written list (a bare JSON array — see PurchaseOrderSteps), the generic
        // MapGetList<TEntity,TKey,TDto>() library route wraps its page in a { items, pageCount, ... } envelope.
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        var items = doc.RootElement.GetProperty("items");
        items.EnumerateArray().Any(e => e.GetProperty("name").GetString() == name).ShouldBeTrue();
    }

    [Then("a log line reports the automated sample product was created")]
    public void ThenALogLineReportsTheAutomatedSampleProductWasCreated() =>
        factory.LogCapture.Messages.ShouldContain(m => m.Contains("AutomatedSample product created", StringComparison.Ordinal));

    [Then(@"the product response was approved by ""(.*)""")]
    public void ThenTheProductResponseWasApprovedBy(string byUser) => Deserialize().UpdatedBy.ShouldBe(byUser);

    [Then("the product response is discontinued")]
    public void ThenTheProductResponseIsDiscontinued() => Deserialize().IsDiscontinued.ShouldBeTrue();

    [Then(@"the product is named ""(.*)""")]
    public void ThenTheProductIsNamed(string name) => Deserialize().Name.ShouldBe(name);

    [Then(@"exactly (\d+) product is named ""(.*)""")]
    public async Task ThenExactlyNProductIsNamed(int count, string name)
    {
        var response = await client.GetAsync("/v1/products");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var matches = doc.RootElement.GetProperty("items").EnumerateArray()
            .Count(e => e.GetProperty("name").GetString() == name);
        matches.ShouldBe(count);
    }

    [Then(@"""(.*)"" is gone")]
    public async Task ThenIsGone(string name)
    {
        var response = await client.GetAsync($"/v1/products/{_productIdsByName[name]}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Then(@"""(.*)"" still exists")]
    public async Task ThenStillExists(string name)
    {
        var response = await client.GetAsync($"/v1/products/{_productIdsByName[name]}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    #endregion

    private async Task CreateNamedAsync(string name, decimal price)
    {
        await CreateAsync(name, price);
        _productIdsByName[name] = _lastId;
    }

    private static string ExtractQuoted(string text)
    {
        var start = text.IndexOf('"') + 1;
        var end = text.IndexOf('"', start);
        return text[start..end];
    }

    private async Task CreateAsync(string name, decimal price)
    {
        state.Response = await client.PostAsJsonAsync("/v1/products", new { name, price });
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();

        if (state.Response.IsSuccessStatusCode)
        {
            var dto = JsonSerializer.Deserialize<ProductDto>(state.ResponseBody, SharedConsts.JsonSerializerOptions);
            _lastId = dto!.Id;
        }
    }

    private ProductDto Deserialize()
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        return JsonSerializer.Deserialize<ProductDto>(state.ResponseBody!, SharedConsts.JsonSerializerOptions)!;
    }
}
