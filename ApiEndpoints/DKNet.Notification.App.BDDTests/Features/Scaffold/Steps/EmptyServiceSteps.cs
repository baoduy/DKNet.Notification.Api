using System.Text.Json;
using DKNet.Notification.App.TestSupport;
using Reqnroll;
using Shouldly;

namespace DKNet.Notification.App.BDDTests.Features.Scaffold.Steps;

/// <summary>
/// Steps for <c>EmptyService.feature</c> (DRK-1994 §5). Every expected status and body is a literal from the
/// spec. The host is built on the first request, after every Given has chosen its settings and caller.
/// </summary>
[Binding]
public sealed class EmptyServiceSteps : IDisposable
{
    private const string DeployedEnvironment = "Production";
    private const string LocalDevelopmentEnvironment = "Development";

    private static readonly Dictionary<string, string> TemplateRoutes = new()
    {
        ["root address"] = "/",
        ["detailed health report"] = "/healthz/detail"
    };

    private static readonly Dictionary<string, string> TemplateSampleLists = new()
    {
        ["products"] = "/v1/products",
        ["purchase orders"] = "/v1/purchase-orders"
    };

    private string? _environment;
    private bool _withValidCaller;
    private ScaffoldApiFactory? _factory;
    private HttpResponseMessage? _response;
    private string? _body;

    #region Given

    [Given("the Notification API is running with no database")]
    public void GivenTheNotificationApiIsRunningWithNoDatabase()
    {
        // The deployed settings name no database server; this host adds none either.
        _environment = DeployedEnvironment;
    }

    [Given("the Notification API is running with its deployed settings")]
    public void GivenTheNotificationApiIsRunningWithItsDeployedSettings() => _environment = DeployedEnvironment;

    [Given("the Notification API is running in the local Development environment")]
    public void GivenTheNotificationApiIsRunningInTheLocalDevelopmentEnvironment() =>
        _environment = LocalDevelopmentEnvironment;

    [Given(@"^the caller ""accounts-api"" holds a valid Entra ID token$")]
    public void GivenTheCallerAccountsApiHoldsAValidEntraIdToken() => _withValidCaller = true;

    #endregion

    #region When

    [When("the cluster's liveness probe asks the health check without a token")]
    public Task WhenTheClustersLivenessProbeAsksTheHealthCheckWithoutAToken() => GetAsync("/healthz");

    [When(@"^""accounts-api"" asks for the (root address|detailed health report)$")]
    public Task WhenAccountsApiAsksForTheTemplateRoute(string templateRoute) =>
        GetAsync(TemplateRoutes[templateRoute]);

    [When(@"^""accounts-api"" asks for the root address without a token$")]
    public Task WhenAccountsApiAsksForTheRootAddressWithoutAToken()
    {
        _withValidCaller.ShouldBeFalse("this step needs a host with no stand-in caller");
        return GetAsync("/");
    }

    [When(@"^""accounts-api"" asks for the template's (products|purchase orders) list$")]
    public Task WhenAccountsApiAsksForTheTemplatesSampleList(string sample) =>
        GetAsync(TemplateSampleLists[sample]);

    #endregion

    #region Then

    [Then(@"^the answer is (\d+)$")]
    public void ThenTheAnswerIs(int statusCode)
    {
        _response.ShouldNotBeNull();
        ((int)_response.StatusCode).ShouldBe(statusCode, $"body: {_body}");
    }

    [Then(@"^the answer is (\d+) with the status ""(.*)""$")]
    public void ThenTheAnswerIsWithTheStatus(int statusCode, string status)
    {
        ThenTheAnswerIs(statusCode);
        using var document = JsonDocument.Parse(_body!);
        document.RootElement.GetProperty("status").GetString().ShouldBe(status);
    }

    [Then("the answer holds no other detail")]
    public void ThenTheAnswerHoldsNoOtherDetail()
    {
        using var document = JsonDocument.Parse(_body!);
        document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["status"]);
        _body.ShouldBe("""{"status":"Healthy"}""");
    }

    #endregion

    private async Task GetAsync(string path)
    {
        _environment.ShouldNotBeNull("a Given step must choose the API's settings first");
        _factory = new ScaffoldApiFactory(_environment, _withValidCaller);
        using var client = _factory.CreateHttpsClient();
        _response = await client.GetAsync(path);
        _body = await _response.Content.ReadAsStringAsync();
    }

    public void Dispose()
    {
        _response?.Dispose();
        _factory?.Dispose();
    }
}
