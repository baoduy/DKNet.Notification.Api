using DKNet.Notification.App.Tests.Client.Support;
using DKNet.Notification.Client;
using DKNet.Notification.Client.Contracts;
using Refit;

namespace DKNet.Notification.App.Tests.Integration.Client;

/// <summary>
/// DRK-1789 Build additions: every client method called against the in-memory API, so request bodies,
/// query strings and responses are proven to bind both ways — the parity check proves only that the
/// routes line up.
/// </summary>
public sealed class ClientRoundTripTests(ClientApiFixture fixture) : IClassFixture<ClientApiFixture>
{
    private ServiceProvider RegisterClient()
    {
        var services = new ServiceCollection();
        services.AddApiClient(new Uri("https://orders.example.com"))
            .ConfigurePrimaryHttpMessageHandler(() => fixture.Server.CreateHandler());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ProductsClient_EveryMethod_RoundTripsThroughTheApi()
    {
        await fixture.ResetDatabaseAsync();
        using var provider = RegisterClient();
        var products = provider.GetRequiredService<IProductsClient>();

        var created = await products.CreateProductAsync(
            new CreateProductRequest { Name = "Stapler", Price = 12.50m, SupplierCostPrice = 10.00m });
        created.Name.ShouldBe("Stapler");
        created.Price.ShouldBe(12.50m);
        created.IsDiscontinued.ShouldBeFalse();
        created.CreatedBy.ShouldNotBeNullOrWhiteSpace();
        created.CreatedOn.ShouldNotBe(default);

        var fetched = await products.GetProductAsync(created.Id);
        fetched.Id.ShouldBe(created.Id);

        var repriced = await products.ChangeProductPriceAsync(created.Id, new ChangeProductPriceRequest { Price = 15.00m });
        repriced.Price.ShouldBe(15.00m);
        repriced.UpdatedOn.ShouldNotBeNull();
        repriced.UpdatedBy.ShouldNotBeNullOrWhiteSpace();

        var approved = await products.ApproveProductAsync(created.Id, new ApproveProductRequest { ByUser = "ops" });
        approved.Id.ShouldBe(created.Id);

        var referenced = await products.AssignSupplierReferenceAsync(
            created.Id, new AssignSupplierReferenceRequest { SupplierReferenceCode = "SUP-001" });
        referenced.SupplierReferenceCode.ShouldBe("SUP-001");

        var page = await products.ListProductsAsync(new ProductListQuery
        {
            Filter = ["price:GreaterThan:10", "name:Equal:Stapler"],
            Search = "Stap",
            OrderBy = "name",
            Desc = true,
            PageNumber = 1,
            PageSize = 5,
            FromDate = DateTimeOffset.UtcNow.AddDays(-1),
            ToDate = DateTimeOffset.UtcNow.AddDays(1)
        });
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(created.Id);
        page.PageNumber.ShouldBe(1);
        page.PageSize.ShouldBe(5);
        page.TotalItemCount.ShouldBe(1);

        var discontinued = await products.DiscontinueProductAsync(
            created.Id, new DiscontinueProductRequest { ReplacementName = "Stapler II", ReplacementPrice = 16.00m });
        discontinued.Id.ShouldBe(created.Id);
        discontinued.IsDiscontinued.ShouldBeTrue();
        var replacement = (await products.ListProductsAsync(new ProductListQuery { Filter = ["name:Equal:Stapler II"] }))
            .Items.ShouldHaveSingleItem();
        replacement.Price.ShouldBe(16.00m);

        // Only a discontinued product can be deleted.
        await products.DeleteProductAsync(created.Id);
        var remaining = await products.ListProductsAsync();
        remaining.Items.Select(p => p.Id).ShouldBe([replacement.Id]);
    }

    [Fact]
    public async Task PurchaseOrdersClient_EveryMethod_RoundTripsThroughTheApi()
    {
        await fixture.ResetDatabaseAsync();
        using var provider = RegisterClient();
        var purchaseOrders = provider.GetRequiredService<IPurchaseOrdersClient>();

        var created = await purchaseOrders.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest { CustomerName = "PO-2001", Amount = 40.00m }, "po-2001-a");
        created.Status.ShouldBe("placed");
        created.CreatedBy.ShouldNotBeNullOrWhiteSpace();

        var fetched = await purchaseOrders.GetPurchaseOrderAsync(created.Id);
        fetched.CustomerName.ShouldBe("PO-2001");

        var updated = await purchaseOrders.UpdatePurchaseOrderAsync(
            created.Id, new UpdatePurchaseOrderRequest { Amount = 45.00m });
        updated.Amount.ShouldBe(45.00m);

        var page = await purchaseOrders.ListPurchaseOrdersAsync(
            new PurchaseOrderListQuery { PageIndex = 1, PageSize = 10, CustomerName = "PO-2001" });
        page.ShouldHaveSingleItem().Id.ShouldBe(created.Id);

        var cancelled = await purchaseOrders.CancelPurchaseOrderAsync(created.Id);
        cancelled.Id.ShouldBe(created.Id);
        cancelled.Status.ShouldBe("cancelled");

        await purchaseOrders.DeletePurchaseOrderAsync(created.Id);
        (await purchaseOrders.ListPurchaseOrdersAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithABlankKey_TheRefusalNamesTheMissingHeader()
    {
        var api = new RecordingApiHandler("{}");
        var services = new ServiceCollection();
        services.AddApiClient(new Uri("https://orders.example.com")).ConfigurePrimaryHttpMessageHandler(() => api);
        using var provider = services.BuildServiceProvider();

        var thrown = await Record.ExceptionAsync(() => provider.GetRequiredService<IPurchaseOrdersClient>()
            .CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest { CustomerName = "PO-2002", Amount = 1.00m }, " "));

        var refusal = thrown.ShouldBeOfType<ApiRequestException>().InnerException.ShouldBeOfType<ArgumentException>();
        refusal.Message.ShouldBe("An idempotency key is required: pass a non-blank value for the X-Idempotency-Key header.");
        api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void AddApiClient_WithNoBaseAddress_IsRefused() =>
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddApiClient(null!))
            .ParamName.ShouldBe("baseAddress");

    [Fact]
    public void AddApiClient_OnNoServiceCollection_IsRefused() =>
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddApiClient(new Uri("https://orders.example.com")))
            .ParamName.ShouldBe("services");

    [Fact]
    public async Task RequestNotBuiltByTheClient_ThroughTheSharedHttpClient_IsNotRefusedForAMissingKey()
    {
        using var provider = RegisterClient();
        var http = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ApiClientServiceCollectionExtensions.HttpClientName);

        using var response = await http.GetAsync(new Uri("/v1/products/summary", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
