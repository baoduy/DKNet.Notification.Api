using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The one scenario whose first call must stay running inside the service. Its step text is the same as a step in
/// <see cref="SendNotificationSteps" />; Reqnroll prefers this binding here because it matches on the feature and
/// the scenario both. A class of its own, because a method-level scope would add to the class-level one, not
/// narrow it.
/// </summary>
[Binding]
[Scope(Feature = SendNotificationSteps.FeatureTitle, Scenario = "A repeat of a call that is still running is refused")]
public sealed class HeldCallSteps(SendScenario scenario)
{
    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task GivenEmailedTemplateWithKeyAndHeld(string caller, string templateId, string key)
    {
        var gate = scenario.Factory.Gate;
        gate.Hold();
        var body = EmailBody(templateId, "jane@example.com");
        scenario.HeldCall = scenario.SendUnrecordedAsync(caller, key, Json(body), body);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
