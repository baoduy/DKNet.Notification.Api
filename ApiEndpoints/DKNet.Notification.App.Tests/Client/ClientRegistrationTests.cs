using DKNet.Notification.App.Tests.Client.Support;
using DKNet.Notification.Client;
using DKNet.Notification.Client.Contracts;
using Refit;

namespace DKNet.Notification.App.Tests.Client;

/// <summary>
/// DRK-1789 §5 <c>@unit</c> client scenarios: the client registered in a bare service collection, with an
/// in-memory stand-in (<see cref="RecordingApiHandler" />) in place of the API — nothing leaves the process.
/// </summary>
public sealed class ClientRegistrationTests
{
    private const string EmptyPage =
        """{"items":[],"pageCount":0,"pageNumber":1,"pageSize":20,"totalItemCount":0,"hasNextPage":false,"hasPreviousPage":false}""";

    private const string CreatedPurchaseOrder =
        """{"id":"0f8fad5b-d9cb-469f-a165-70867728950e","customerName":"PO-1002","amount":50.00,"status":"placed","createdBy":"billing-service"}""";

    private static ServiceProvider RegisterClient(RecordingApiHandler api, Func<DelegatingHandler>? handlerFactory = null)
    {
        var services = new ServiceCollection();
        services.AddApiClient(new Uri("https://orders.example.com"), handlerFactory)
            .ConfigurePrimaryHttpMessageHandler(() => api);
        return services.BuildServiceProvider();
    }

    #region Scenario: With no handler the client sends no credential

    [Fact]
    public async Task ListProducts_RegisteredWithNoHandler_TheRequestCarriesNoCredential()
    {
        var api = new RecordingApiHandler(EmptyPage);
        using var provider = RegisterClient(api);
        var products = provider.GetRequiredService<IProductsClient>();

        await products.ListProductsAsync();

        var sent = api.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe("GET");
        sent.Path.TrimEnd('/').ShouldBe("/v1/products");
        sent.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task ListProducts_RegisteredWithTheCallersHandler_TheRequestCarriesOnlyThatCredential()
    {
        var api = new RecordingApiHandler(EmptyPage);
        using var provider = RegisterClient(api, () => new BearerTokenHandler("billing-service-token"));
        var products = provider.GetRequiredService<IProductsClient>();

        await products.ListProductsAsync();

        api.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("Bearer billing-service-token");
    }

    #endregion

    #region Scenario: Creating a purchase order without a key is refused before sending

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePurchaseOrder_WithNoKey_IsRefusedByTheClient_AndNoRequestReachesTheApi(string? key)
    {
        var api = new RecordingApiHandler(CreatedPurchaseOrder);
        using var provider = RegisterClient(api);
        var purchaseOrders = provider.GetRequiredService<IPurchaseOrdersClient>();

        var thrown = await Record.ExceptionAsync(() => purchaseOrders.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest { CustomerName = "PO-1002", Amount = 50.00m },
            key!));

        // Refused with an ArgumentException — thrown directly, or wrapped by Refit's ApiRequestException
        // when the refusal is raised inside the client's HTTP pipeline.
        thrown.ShouldNotBeNull("the client should refuse a create with no idempotency key");
        var refusal = thrown as ArgumentException ?? (thrown as ApiRequestException)?.InnerException as ArgumentException;
        refusal.ShouldNotBeNull($"expected an ArgumentException refusal, got {thrown.GetType().FullName}");
        api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithAKey_SendsExactlyOneRequestCarryingThatKey()
    {
        var api = new RecordingApiHandler(CreatedPurchaseOrder);
        using var provider = RegisterClient(api);
        var purchaseOrders = provider.GetRequiredService<IPurchaseOrdersClient>();

        var created = await purchaseOrders.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest { CustomerName = "PO-1002", Amount = 50.00m },
            "po-1002-a");

        created.CustomerName.ShouldBe("PO-1002");
        var sent = api.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe("POST");
        sent.Path.TrimEnd('/').ShouldBe("/v1/purchase-orders");
        sent.IdempotencyKey.ShouldBe("po-1002-a");
    }

    #endregion
}
