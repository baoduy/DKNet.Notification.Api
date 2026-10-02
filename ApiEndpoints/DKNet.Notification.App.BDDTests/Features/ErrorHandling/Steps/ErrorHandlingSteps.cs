namespace DKNet.Notification.App.BDDTests.Features.ErrorHandling.Steps;

[Binding]
public sealed class ErrorHandlingSteps(HttpClient client, ScenarioState state)
{
    #region Given

    [Given(@"a service generated from the `DKNet\.Templates` starter registers the standard error setting and runs in production")]
    public void GivenAServiceRegistersTheStandardErrorSettingAndRunsInProduction()
    {
        // Program.cs calls AddFluentValidationConfig() unconditionally on every boot, and TestApiFactoryBase
        // already runs this host in the "Testing" environment — non-Development, which is what R3 needs.
    }

    [Given(@"a service generated from the `DKNet\.Templates` starter registers a setting that answers 409 for a failure marked ""precondition""")]
    public void GivenAServiceRegistersAPreconditionSetting()
    {
        // Same registered app as above — FluentValidationConfig's existing StatusCode rule already answers
        // 409 for any failure whose code starts with PreconditionCodes.Prefix.
    }

    #endregion

    #region When

    [When("storefront sends a request that raises an unexpected error")]
    public Task WhenStorefrontSendsARequestThatRaisesAnUnexpectedError() =>
        GetAsync(TestOnlyRoutes.UnexpectedErrorPath);

    [When(@"an endpoint of that service answers a command failure marked ""precondition""")]
    public Task WhenAnEndpointAnswersACommandFailureMarkedPrecondition() =>
        GetAsync(TestOnlyRoutes.PreconditionFailurePath);

    #endregion

    #region Then

    [Then(@"the response body carries the title ""Error"", a status, a type, a trace identifier and an error list in the standard shape")]
    public void ThenTheResponseBodyCarriesTheStandardErrorShape()
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        var root = doc.RootElement;

        root.GetProperty("title").GetString().ShouldBe("Error");

        root.TryGetProperty("status", out var status).ShouldBeTrue();
        status.ValueKind.ShouldBe(JsonValueKind.Number);

        root.TryGetProperty("type", out var type).ShouldBeTrue();
        type.GetString().ShouldNotBeNullOrEmpty();

        root.TryGetProperty("traceId", out var traceId).ShouldBeTrue();
        traceId.GetString().ShouldNotBeNullOrEmpty();

        root.TryGetProperty("errors", out var errors).ShouldBeTrue();
        errors.ValueKind.ShouldBe(JsonValueKind.Array);
        errors.GetArrayLength().ShouldBeGreaterThan(0);

        errors[0].TryGetProperty("message", out var message).ShouldBeTrue();
        message.GetString().ShouldNotBeNullOrEmpty();
    }

    #endregion

    private async Task GetAsync(string path)
    {
        state.Response = await client.GetAsync(path);
        state.ResponseBody = await state.Response.Content.ReadAsStringAsync();
    }
}
