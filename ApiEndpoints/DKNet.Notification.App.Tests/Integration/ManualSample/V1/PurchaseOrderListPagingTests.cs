using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.App.Tests.Integration.Support;
using DKNet.Notification.AppServices.ManualSample.V1;
using DKNet.Notification.Domains.Features.ManualSample.Entities;
using DKNet.Notification.Share;

namespace DKNet.Notification.App.Tests.Integration.ManualSample.V1;

/// <summary>
/// <c>ListPurchaseOrdersQuery.PageSize</c> declares a default of 20 via its property initializer, but the GET
/// route binds the query via <c>[AsParameters]</c> — which assigns every property from the query string,
/// including ones the caller never supplied, and can overwrite that initializer with the CLR default of 0
/// rather than leaving it alone (DRK-738 finding #7). <c>ListPurchaseOrdersQueryValidator</c>'s
/// <c>InclusiveBetween(1, 100)</c> then rejects that 0 as out of range, so an un-parameterised
/// <c>GET /v1/purchase-orders</c> — every pre-existing test in this repo always passes an explicit
/// <c>pageIndex</c>/<c>pageSize</c> and would never catch this — would 400 instead of serving the declared
/// default page.
/// </summary>
public sealed class PurchaseOrderListPagingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string BaseUrl = "/v1/purchase-orders";

    [Fact]
    public async Task List_WithNoQueryString_ShouldSucceed_UsingTheDeclaredDefaultPageSize()
    {
        await fixture.ResetDatabaseAsync();
        await SeedOrdersAsync(25);
        var client = fixture.CreateClient();

        using var response = await client.GetAsync(BaseUrl);
        using var explicitFirstPageResponse = await client.GetAsync($"{BaseUrl}?pageIndex=1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            "the un-parameterised listing must serve the declared default page, not reject it as an invalid page size.");
        explicitFirstPageResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var orders = await response.Content.ReadFromJsonAsync<List<PurchaseOrderDto>>(SharedConsts.JsonSerializerOptions);
        var explicitFirstPageOrders = await explicitFirstPageResponse.Content.ReadFromJsonAsync<List<PurchaseOrderDto>>(SharedConsts.JsonSerializerOptions);
        orders.ShouldNotBeNull();
        orders!.Count.ShouldBe(20, "PageSize's declared default is 20 — a smaller/larger count means it was not honoured.");
        orders.Select(o => o.Id).ShouldBe(explicitFirstPageOrders!.Select(o => o.Id),
            "omitting pageIndex must serve the same page 1 an explicit pageIndex=1 (DefaultPageIndex) serves.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_WithPageSizeOutsideDeclaredRange_ShouldReturnBadRequest(int pageSize)
    {
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var response = await client.GetAsync($"{BaseUrl}?pageIndex=1&pageSize={pageSize}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task List_WithPageIndexOutsideDeclaredRange_ShouldReturnBadRequest_NotThrow(int pageIndex)
    {
        // Before the DRK-744 fix this path 500s — X.PagedList throws on pageNumber < 1 — so an assertion
        // that only checked "not 200" would have passed on `dev`. Assert the same failure shape ?pageSize=0
        // produces, not just the status code.
        await fixture.ResetDatabaseAsync();
        var client = fixture.CreateClient();

        using var pageIndexResponse = await client.GetAsync($"{BaseUrl}?pageIndex={pageIndex}");
        using var pageSizeResponse = await client.GetAsync($"{BaseUrl}?pageSize=0");

        pageIndexResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var pageIndexBody = JsonNode.Parse(await pageIndexResponse.Content.ReadAsStringAsync())!.AsObject();
        var pageSizeBody = JsonNode.Parse(await pageSizeResponse.Content.ReadAsStringAsync())!.AsObject();
        pageIndexBody.Select(p => p.Key).Order().ShouldBe(pageSizeBody.Select(p => p.Key).Order(),
            "an unvalidated pageIndex must fail with the same ValidationProblemDetails shape as pageSize, not a 500's ProblemDetails shape.");
        pageIndexBody["errors"].ShouldNotBeNull();
    }

    private async Task SeedOrdersAsync(int count)
    {
        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepositorySpec>();
        for (var i = 0; i < count; i++)
        {
            await repository.AddAsync(new PurchaseOrder($"Customer {i}", 10m, "seed"), CancellationToken.None);
        }

        await repository.SaveChangesAsync(CancellationToken.None);
    }
}
