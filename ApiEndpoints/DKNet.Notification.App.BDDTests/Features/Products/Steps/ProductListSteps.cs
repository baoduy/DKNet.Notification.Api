using System.Globalization;
using System.Linq;
using System.Net.Http.Json;

namespace DKNet.Notification.App.BDDTests.Features.Products.Steps;

/// <summary>
/// Steps for the generic list-query contract (filter/search/order/page) exposed by
/// <c>MapGetList&lt;Product, Guid, ProductDto&gt;()</c>. Kept apart from <see cref="ProductSteps"/>: those
/// prove the CRUD lifecycle, these fence the read-query surface. See docs/generic-list-endpoint.md.
/// </summary>
[Binding]
public sealed class ProductListSteps(HttpClient client, ScenarioState state)
{
    #region Given

    [Given("the following products exist:")]
    public async Task GivenTheFollowingProductsExist(Table table)
    {
        foreach (var row in table.Rows)
        {
            var name = row["name"];
            var price = decimal.Parse(row["price"], CultureInfo.InvariantCulture);
            var response = await client.PostAsJsonAsync("/v1/products", new { name, price });
            response.IsSuccessStatusCode.ShouldBeTrue($"seeding product '{name}' should succeed");
        }
    }

    #endregion

    #region When

    [When(@"I list products with query ""(.*)""")]
    public async Task WhenIListProductsWithQuery(string query)
    {
        state.Response = await client.GetAsync($"/v1/products{query}");
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }

    [When(@"I list products with a fromDate (\d+) hours in the past")]
    public async Task WhenIListProductsWithAFromDateHoursInThePast(int hours) =>
        await WhenIListProductsWithQuery($"?fromDate={RelativeIso(-hours)}");

    [When(@"I list products with a toDate (\d+) hours in the past")]
    public async Task WhenIListProductsWithAToDateHoursInThePast(int hours) =>
        await WhenIListProductsWithQuery($"?toDate={RelativeIso(-hours)}");

    [When(@"I list products with a fromDate (\d+) hours in the future and a toDate (\d+) hours in the past")]
    public async Task WhenIListProductsWithAnImpossibleWindow(int fromHours, int toHours) =>
        await WhenIListProductsWithQuery($"?fromDate={RelativeIso(fromHours)}&toDate={RelativeIso(-toHours)}");

    #endregion

    private static string RelativeIso(int hoursOffset) =>
        Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(hoursOffset).ToString("O", CultureInfo.InvariantCulture));

    #region Then

    [Then(@"the list contains exactly (\d+) products")]
    public void ThenTheListContainsExactlyNProducts(int count) => ItemNames().Count.ShouldBe(count);

    [Then(@"the list contains exactly ""(.*)""")]
    public void ThenTheListContainsExactly(string names)
    {
        var expected = Split(names);
        // Order-independent set equality — pins both inclusion AND exclusion AND the total count, so a
        // filter that returns too many or too few rows fails here.
        ItemNames().OrderBy(n => n).ShouldBe(expected.OrderBy(n => n));
    }

    [Then(@"the listed products are in order ""(.*)""")]
    public void ThenTheListedProductsAreInOrder(string names) => ItemNames().ShouldBe(Split(names));

    [Then(@"the paged response ""(.*)"" is ""(.*)""")]
    public void ThenThePagedResponseFieldIs(string field, string expected)
    {
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        // GetRawText() yields the literal JSON token — "4", "100", "true", "false" — matching the values
        // the feature file asserts for numeric and boolean envelope fields.
        doc.RootElement.GetProperty(field).GetRawText().ShouldBe(expected);
    }

    [Then(@"the response names the unsupported field ""(.*)""")]
    public void ThenTheResponseNamesTheUnsupportedField(string field)
    {
        // The generic list route answers an unusable filter/order field via Results.Problem(error, 400) —
        // ProblemDetails' "detail" carries the field name, not an "errors" array of { message, code?, field? }
        // (that shape is AddErrorResponses', a different refusal path this route never goes through).
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        doc.RootElement.TryGetProperty("detail", out var detail).ShouldBeTrue();
        detail.GetString()!.Contains(field, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    #endregion

    private List<string> ItemNames()
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        return doc.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
    }

    private static string[] Split(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
