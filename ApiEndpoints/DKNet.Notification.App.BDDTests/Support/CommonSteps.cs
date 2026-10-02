namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// Steps shared by more than one feature file. Kept in one binding so the exact step text is not duplicated —
/// and made ambiguous — across per-feature step classes.
/// </summary>
[Binding]
public sealed class CommonSteps(ScenarioState state)
{
    [Given("the service is running with no Redis connection configured")]
    public void GivenTheServiceIsRunningWithNoRedisConnectionConfigured()
    {
        // The BDD host never sets ConnectionStrings:Redis — this is the default, already-in-effect state.
    }

    [Then(@"the response is (\d+)")]
    public void ThenTheResponseIs(int statusCode)
    {
        state.Response.ShouldNotBeNull();
        ((int)state.Response!.StatusCode).ShouldBe(statusCode);
    }
}
