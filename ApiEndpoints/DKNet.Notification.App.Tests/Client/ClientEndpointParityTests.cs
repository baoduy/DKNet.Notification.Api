using DKNet.Notification.App.Tests.Client.Support;

namespace DKNet.Notification.App.Tests.Client;

/// <summary>
/// DRK-1789 §5 — the endpoint check that keeps the client in step with the API. Runs against the live
/// endpoint list of the in-memory API; the drift scenarios run the same check against that list plus or
/// minus the named endpoint, so no endpoint has to be added to or removed from the template itself.
/// </summary>
public sealed class ClientEndpointParityTests(ClientApiFixture fixture) : IClassFixture<ClientApiFixture>
{
    #region Scenario: Every endpoint has exactly one client method

    [Fact]
    public void EveryEndpoint_HasExactlyOneClientMethod_AndEveryClientMethodMatchesExactlyOneEndpoint()
    {
        var apiEndpoints = EndpointParity.ApiEndpoints(fixture.Services);
        var clientMethods = EndpointParity.ClientMethods();
        apiEndpoints.ShouldContain(new ApiEndpoint("GET", "/v1/products/summary"));
        apiEndpoints.ShouldContain(new ApiEndpoint("POST", "/v1/purchase-orders"));
        clientMethods.ShouldNotBeEmpty();

        var report = EndpointParity.Compare(apiEndpoints, clientMethods);

        report.EndpointsWithoutClientMethod.ShouldBeEmpty(report.FailureMessage);
        report.EndpointsWithMoreThanOneClientMethod.ShouldBeEmpty(report.FailureMessage);
        report.ClientMethodsWithoutEndpoint.ShouldBeEmpty(report.FailureMessage);
        report.IsInStep.ShouldBeTrue(report.FailureMessage);
    }

    #endregion

    #region Scenario: An endpoint added without a client method fails the tests

    [Fact]
    public void EndpointAddedWithoutAClientMethod_FailsTheCheck_NamingTheProductsExportEndpoint()
    {
        var productsExport = new ApiEndpoint("GET", "/v1/products/export");
        var apiEndpoints = EndpointParity.ApiEndpoints(fixture.Services);
        apiEndpoints.ShouldNotContain(productsExport);

        var report = EndpointParity.Compare(apiEndpoints.Append(productsExport), EndpointParity.ClientMethods());

        report.IsInStep.ShouldBeFalse();
        report.EndpointsWithoutClientMethod.ShouldBe(["GET /v1/products/export"]);
        report.EndpointsWithMoreThanOneClientMethod.ShouldBeEmpty();
        report.ClientMethodsWithoutEndpoint.ShouldBeEmpty();
        report.FailureMessage.ShouldBe("Endpoints with no client method: GET /v1/products/export");
    }

    #endregion

    #region Scenario: An endpoint removed while its client method stays fails the tests

    [Fact]
    public void EndpointRemovedWhileItsClientMethodStays_FailsTheCheck_NamingTheCancelPurchaseOrderClientMethod()
    {
        var cancelPurchaseOrder = new ApiEndpoint("POST", "/v1/purchase-orders/{id}/cancel");
        var apiEndpoints = EndpointParity.ApiEndpoints(fixture.Services);
        apiEndpoints.ShouldContain(cancelPurchaseOrder);

        var report = EndpointParity.Compare(
            apiEndpoints.Where(e => e != cancelPurchaseOrder),
            EndpointParity.ClientMethods());

        report.IsInStep.ShouldBeFalse();
        report.EndpointsWithoutClientMethod.ShouldBeEmpty();
        report.EndpointsWithMoreThanOneClientMethod.ShouldBeEmpty();
        report.ClientMethodsWithoutEndpoint.ShouldBe(["IPurchaseOrdersClient.CancelPurchaseOrderAsync"]);
        report.FailureMessage.ShouldBe(
            "Client methods that match no endpoint: IPurchaseOrdersClient.CancelPurchaseOrderAsync");
    }

    #endregion

    #region Scenario Outline: Addresses that are not API endpoints get no client method

    [Theory]
    [InlineData("public status probe", "/healthz")]
    [InlineData("root status probe", "/")]
    [InlineData("detailed health report", "/healthz/detail")]
    [InlineData("OpenAPI document", "/openapi/{documentName}.json")]
    [InlineData("docs browser", "/docs/{documentName}")]
    public void AddressThatIsNotAnApiEndpoint_GetsNoClientMethod_AndTheCheckStillPasses(string address, string path)
    {
        var apiEndpoints = EndpointParity.ApiEndpoints(fixture.Services);
        var clientMethods = EndpointParity.ClientMethods();
        apiEndpoints.Select(e => e.Path).ShouldContain(path, $"the API should serve the {address} at {path}");
        EndpointParity.IsNonApiAddress(path).ShouldBeTrue($"the {address} is not an API endpoint");

        clientMethods.Where(m => m.Path is not null && m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty($"the client should offer no method for the {address}");

        var report = EndpointParity.Compare(apiEndpoints, clientMethods);
        report.IsInStep.ShouldBeTrue(report.FailureMessage);
    }

    #endregion
}
