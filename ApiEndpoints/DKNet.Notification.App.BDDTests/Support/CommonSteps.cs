using System.Linq;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// Steps shared by more than one DRK-455 feature file (the PurchaseOrder and Product scenarios use
/// the same precondition and rejection wording). Kept in one binding so the exact step text is not
/// duplicated — and made ambiguous — across per-feature step classes.
/// </summary>
[Binding]
public sealed class CommonSteps(ScenarioState state)
{
    [Given("the service is running with no Redis connection configured")]
    public void GivenTheServiceIsRunningWithNoRedisConnectionConfigured()
    {
        // The BDD host never sets ConnectionStrings:Redis — this is the default, already-in-effect state.
    }

    [Then("the request is rejected")]
    public void ThenTheRequestIsRejected()
    {
        state.Response.ShouldNotBeNull();
        state.Response!.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Then(@"the response status is (\d+)")]
    public void ThenTheResponseStatusIs(int statusCode)
    {
        state.Response.ShouldNotBeNull();
        ((int)state.Response!.StatusCode).ShouldBe(statusCode);
    }

    // DRK-1410: the spec's own Gherkin wording ("the response is <code>") differs from this repo's
    // pre-existing wording above ("the response status is <code>") — kept as a separate step rather than
    // reworded, since the acceptance criteria are frozen text, not paraphrased to match house style.
    [Then(@"the response is (\d+)")]
    public void ThenTheResponseIs(int statusCode)
    {
        state.Response.ShouldNotBeNull();
        ((int)state.Response!.StatusCode).ShouldBe(statusCode);
    }

    // DRK-1410: the BDD host runs with FeatureManagement:RequireAuthorization=false (BddApiFactory), so
    // no policy is ever evaluated here — "holds the scope" is satisfied by the route staying reachable at
    // all. Scope-gated variants belong in DKNet.Notification.App.Tests/Integration against AuthOnApiFixture instead.
    [Given(@"catalogue-ops holds the scope ""(.*)""")]
    public void GivenCatalogueOpsHoldsTheScope(string scope)
    {
    }

    [Given("catalogue-ops holds every scope the operation needs")]
    public void GivenCatalogueOpsHoldsEveryScopeTheOperationNeeds()
    {
    }

    [Given("catalogue-ops is signed in")]
    public void GivenCatalogueOpsIsSignedIn()
    {
    }

    [Then("the response body carries a trace identifier")]
    public void ThenTheResponseBodyCarriesATraceIdentifier()
    {
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        doc.RootElement.TryGetProperty("traceId", out var traceId).ShouldBeTrue();
        traceId.GetString().ShouldNotBeNullOrEmpty();
    }

    [Then("the response body carries a code naming the rule that refused")]
    public void ThenTheResponseBodyCarriesACodeNamingTheRuleThatRefused()
    {
        // A "code" that's merely non-empty passes even when every refusal shares one generic value —
        // it must carry the "precondition." prefix (R... one shared error-response setting) plus a rule
        // segment after it, not just the prefix alone.
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        doc.RootElement.TryGetProperty("code", out var code).ShouldBeTrue();
        var value = code.GetString();
        value.ShouldNotBeNullOrEmpty();
        value!.ShouldStartWith("precondition.");
        value.Length.ShouldBeGreaterThan("precondition.".Length);
    }

    [Then("the response names the field it refused")]
    public void ThenTheResponseNamesTheFieldItRefused()
    {
        // This scenario's only refusal is ListPurchaseOrdersQueryValidator's PageIndex rule — assert the
        // errors list names that field, not merely that it names some field.
        state.ResponseBody.ShouldNotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(state.ResponseBody!);
        doc.RootElement.TryGetProperty("errors", out var errors).ShouldBeTrue();
        errors.EnumerateArray()
            .Any(e => e.TryGetProperty("field", out var field) &&
                      field.GetString()!.Contains("pageindex", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue();
    }
}
