using System.Net.Http.Json;
using DKNet.Notification.App.Tests.Client.Support;
using DKNet.Notification.Client;
using DKNet.Notification.Client.Contracts;
using Refit;

namespace DKNet.Notification.App.Tests.Client;

/// <summary>
/// DRK-1789 §5 <c>@integration</c> client scenarios: a downstream system ("billing-service") registers the
/// client with its one registration call and calls the in-memory API through it. Credential and
/// idempotency key are observed where the API receives them (<see cref="ClientApiFixture.ReceivedRequests" />).
/// </summary>
public sealed class ClientCallTests(ClientApiFixture fixture) : IClassFixture<ClientApiFixture>
{
    private static readonly Uri OrdersBaseAddress = new("https://orders.example.com");

    /// <summary>Registers the client the way billing-service does, with the in-memory API as the network.</summary>
    private ServiceProvider RegisterClient(Func<DelegatingHandler>? handlerFactory = null)
    {
        var services = new ServiceCollection();
        services.AddApiClient(OrdersBaseAddress, handlerFactory)
            .ConfigurePrimaryHttpMessageHandler(() => fixture.Server.CreateHandler());
        return services.BuildServiceProvider();
    }

    private async Task ResetAsync()
    {
        await fixture.ResetDatabaseAsync();
        fixture.ReceivedRequests.Clear();
    }

    #region Scenario: A downstream system reads a product summary through the client

    [Fact]
    public async Task ProductSummary_ReadThroughTheClient_ReturnsCountOneAndAveragePrice250()
    {
        await ResetAsync();
        using var seed = await fixture.CreateClient()
            .PostAsJsonAsync("/v1/products", new { name = "Paper Clip", price = 2.50m });
        seed.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var provider = RegisterClient();
        var products = provider.GetRequiredService<IProductsClient>();

        var summary = await products.GetProductSummaryAsync();

        summary.ProductCount.ShouldBe(1);
        summary.AveragePrice.ShouldBe(2.50m);
    }

    #endregion

    #region Scenario: The caller's own credential reaches the API

    [Fact]
    public async Task ListProducts_WithTheCallersBearerTokenHandler_TheApiReceivesThatBearerToken()
    {
        await ResetAsync();
        using var provider = RegisterClient(() => new BearerTokenHandler("billing-service-token"));
        var products = provider.GetRequiredService<IProductsClient>();

        var page = await products.ListProductsAsync();

        page.Items.ShouldBeEmpty();
        var received = fixture.ReceivedRequests.ShouldHaveSingleItem();
        received.Method.ShouldBe("GET");
        received.Path.TrimEnd('/').ShouldBe("/v1/products");
        received.Authorization.ShouldBe("Bearer billing-service-token");
    }

    #endregion

    #region Scenario Outline: A refused call gives the caller the API's status and details

    [Fact]
    public async Task ListProducts_FilteredByAFieldTheApiDoesNotOffer_FailsWith400AndTheApisProblemDetails()
    {
        await ResetAsync();
        using var provider = RegisterClient();
        var products = provider.GetRequiredService<IProductsClient>();

        var thrown = await Record.ExceptionAsync(() =>
            products.ListProductsAsync(new ProductListQuery { Filter = ["colour:Equal:red"] }));

        var refused = thrown.ShouldBeAssignableTo<ApiException>();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await refused.GetContentAsAsync<ProblemDetailsResponse>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(400);
        problem.Title.ShouldBe("Bad Request");
        problem.Detail.ShouldBe("Cannot filter by 'colour': no such field on ProductDto.");
    }

    [Fact]
    public async Task GetProduct_WithAnUnknownId_FailsWith404AndTheBodyTheApiReturned()
    {
        await ResetAsync();
        using var provider = RegisterClient();
        var products = provider.GetRequiredService<IProductsClient>();

        var thrown = await Record.ExceptionAsync(() => products.GetProductAsync(Guid.NewGuid()));

        var refused = thrown.ShouldBeAssignableTo<ApiException>();
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The API answers an unknown product with a bare 404 and no body (it maps no status-code
        // problem details), so the details the caller can read are exactly that empty body.
        refused.Content.ShouldBeNullOrEmpty();
    }

    #endregion

    #region Scenario: Creating a purchase order sends the idempotency key

    [Fact]
    public async Task CreatePurchaseOrder_WithKeyPo1001a_TheApiReceivesTheKey_AndTheOrderIsCreated()
    {
        await ResetAsync();
        using var provider = RegisterClient();
        var purchaseOrders = provider.GetRequiredService<IPurchaseOrdersClient>();

        var created = await purchaseOrders.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest { CustomerName = "PO-1001", Amount = 100.00m },
            "po-1001-a");

        var received = fixture.ReceivedRequests.ShouldHaveSingleItem();
        received.Method.ShouldBe("POST");
        received.Path.TrimEnd('/').ShouldBe("/v1/purchase-orders");
        received.IdempotencyKey.ShouldBe("po-1001-a");

        created.CustomerName.ShouldBe("PO-1001");
        created.Amount.ShouldBe(100.00m);
        using var stored = await fixture.CreateClient().GetAsync($"/v1/purchase-orders/{created.Id}");
        stored.StatusCode.ShouldBe(HttpStatusCode.OK);
        var storedOrder = await stored.Content.ReadFromJsonAsync<JsonElement>();
        storedOrder.GetProperty("customerName").GetString().ShouldBe("PO-1001");
    }

    #endregion
}
