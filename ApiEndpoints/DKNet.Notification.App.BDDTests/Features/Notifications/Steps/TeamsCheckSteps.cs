using Microsoft.Extensions.Logging;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.EmailDeliverySteps;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.TeamsScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The Then steps of <c>TeamsChannel.feature</c> (DRK-2035 §5) about the answer to a call: accepted, refused or
/// skipped, what was queued, the start-up entries, the mail catcher and the health check. Every expected value is a
/// literal from the spec. A refused call names its field as the email checks do: <c>teamsDestination</c> for the
/// destination (as email names <c>to</c>), <c>parameters.{name}</c> for a missing parameter.
/// </summary>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class TeamsCheckSteps(SendScenario scenario, TeamsScenario teams)
{
    #region Then — accepted and refused

    [Then(@"^the call is accepted with a new notification id$")]
    public void ThenTheCallIsAcceptedWithANewNotificationId() => scenario.ShouldBeAccepted(scenario.LastAnswer);

    [Then(@"^the call is refused with ""([^""]*)"" naming the Teams destination$")]
    public void ThenTheCallIsRefusedNamingTheTeamsDestination(string code) =>
        ShouldNameTheField(code, "teamsDestination");

    [Then(@"^the call is refused with ""([^""]*)"" naming the parameter ""([^""]*)""$")]
    public void ThenTheCallIsRefusedNamingTheParameter(string code, string parameter) =>
        ShouldNameTheField(code, $"parameters.{parameter}");

    [Then(@"^the call is refused with ""([^""]*)"" naming no field, and nothing is queued$")]
    public void ThenTheCallIsRefusedNamingNoFieldAndNothingIsQueued(string code)
    {
        ShouldBeRefusedWith(scenario.LastAnswer, code);
        using var json = JsonDocument.Parse(scenario.LastAnswer.Body);
        var error = json.RootElement.GetProperty("errors").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe(code);
        (error.TryGetProperty("field", out var field) ? field.GetString() : null).ShouldBeNullOrEmpty();
        ThenNothingIsQueued();
    }

    [Then(@"^the call is refused with ""([^""]*)""$")]
    public void ThenTheCallIsRefusedWith(string code) =>
        ShouldBeRefusedWith(scenario.LastAnswer, HttpStatusCode.ServiceUnavailable, code);

    [Then(@"^nothing is queued$")]
    public void ThenNothingIsQueued()
    {
        scenario.Entries(QueuedEvent).ShouldBeEmpty();
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(0);
    }

    [Then(@"^nothing is queued, and the webhook stub receives nothing$")]
    public async Task ThenNothingIsQueuedAndTheWebhookStubReceivesNothing()
    {
        ThenNothingIsQueued();
        await teams.ShouldReceiveNothingAsync();
    }

    #endregion

    #region Then — skipped

    [Then(@"^1 skip warning is logged with reason ""([^""]*)"", and it does not hold ""([^""]*)""$")]
    public void Then1SkipWarningIsLoggedWithReasonAndItDoesNotHold(string reason, string value)
    {
        // The call did name the value the warning must not hold.
        scenario.LastCall.ShouldNotBeNull().Body.ShouldNotBeNull().ShouldContain($"\"teamsDestination\":\"{value}\"");
        var entry = SkipEntryOf(scenario.LastAnswer, "teams", reason);
        TextsOf(entry).ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"the skip warning holds {value}");
    }

    [Then(@"^the skip warning names channel ""([^""]*)"" and reason ""([^""]*)"", and the webhook stub receives nothing$")]
    public async Task ThenTheSkipWarningNamesChannelAndReasonAndTheWebhookStubReceivesNothing(string channel, string reason)
    {
        SkipEntryOf(scenario.LastAnswer, channel, reason);
        await teams.ShouldReceiveNothingAsync();
    }

    [Then(@"^the call to ""([^""]*)"" is skipped with reason ""([^""]*)""$")]
    public void ThenTheCallToIsSkippedWithReason(string destination, string reason) =>
        SkipEntryOf(teams.AnswerOf[destination], "teams", reason);

    [Then(@"^the call is skipped with reason ""([^""]*)""$")]
    public void ThenTheCallIsSkippedWithReason(string reason) => SkipEntryOf(scenario.LastAnswer, "teams", reason);

    [Then(@"^the Teams call is skipped with reason ""([^""]*)"", and the webhook stub receives nothing$")]
    public async Task ThenTheTeamsCallIsSkippedWithReasonAndTheWebhookStubReceivesNothing(string reason)
    {
        SkipEntryOf(teams.AnswerOf["ops-alerts"], "teams", reason);
        await teams.ShouldReceiveNothingAsync();
    }

    [Then(@"^the webhook stub receives nothing$")]
    public async Task ThenTheWebhookStubReceivesNothing() => await teams.ShouldReceiveNothingAsync();

    #endregion

    #region Then — email, start-up and health

    [Then(@"^the mail catcher receives nothing$")]
    public async Task ThenTheMailCatcherReceivesNothing()
    {
        teams.MailCatcher.IsRunning.ShouldBeTrue("the mail catcher must be ready to receive");
        // A grace past a delivery, so a mail sent would show.
        await Task.Delay(TimeSpan.FromSeconds(2));
        (await teams.MailCatcher.MailCountAsync()).ShouldBe(0);
    }

    [Then(@"^the mail catcher receives 1 mail to ""([^""]*)"", and no start-up entry is about Teams$")]
    public async Task ThenTheMailCatcherReceives1MailToAndNoStartupEntryIsAboutTeams(string to)
    {
        var mails = await TeamsScenario.EventuallyAsync(
            async () =>
            {
                var found = (await teams.MailCatcher.MailsAsync()).Where(m => m.To.Any(r => r.Address == to)).ToArray();
                return found.Length > 0 ? found : null;
            },
            TimeSpan.FromSeconds(15),
            $"the mail catcher got no mail to {to}");
        mails.ShouldHaveSingleItem();

        // The capture saw the start-up (email's entry is there), and no entry of it names Teams.
        scenario.StartupEntries.ShouldNotBeEmpty();
        foreach (var entry in scenario.StartupEntries)
        {
            TextsOf(entry).Append(entry.EventId.Name ?? string.Empty).Append(entry.Category)
                .ShouldAllBe(text => !text.Contains("teams", StringComparison.OrdinalIgnoreCase), $"a start-up entry is about Teams: {entry.Message}");
        }
    }

    [Then(@"^the answer is (\d+) with the status ""([^""]*)""$")]
    public void ThenTheAnswerIsWithTheStatus(int status, string healthStatus)
    {
        var answer = teams.ProbeAnswer.ShouldNotBeNull();
        answer.Status.ShouldBe((HttpStatusCode)status, answer.Body);
        using var json = JsonDocument.Parse(answer.Body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["status"]);
        json.RootElement.GetProperty("status").GetString().ShouldBe(healthStatus);
        teams.WebhookStub.IsRunning.ShouldBeFalse("the webhook stub must still be stopped");
    }

    #endregion

    /// <summary>The one skip warning of an accepted call, with the fields the spec names, and its count.</summary>
    private CapturedLogEntry SkipEntryOf(Answer answer, string channel, string reason)
    {
        var id = scenario.ShouldBeAccepted(answer);
        var entry = scenario.SkipEntries.Where(e => e.Value("NotificationId") == id).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Value("Reason").ShouldBe(reason);
        entry.Value("Channel").ShouldBe(channel);
        entry.Value("CallerId").ShouldBe(Caller);
        entry.Value("TraceId").ShouldNotBeNullOrWhiteSpace();
        scenario.Metrics.Sum(AcceptedCounter, ("channel", channel), ("outcome", "skipped")).ShouldBeGreaterThanOrEqualTo(1);
        scenario.Entries(QueuedEvent).Where(e => e.Value("NotificationId") == id).ShouldBeEmpty();
        return entry;
    }

    /// <summary>Exactly one 400 error, with <paramref name="code" /> and <paramref name="field" /> as the field at fault.</summary>
    private void ShouldNameTheField(string code, string field)
    {
        ShouldBeRefusedWith(scenario.LastAnswer, code);
        using var json = JsonDocument.Parse(scenario.LastAnswer.Body);
        var error = json.RootElement.GetProperty("errors").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe(code);
        error.GetProperty("field").GetString().ShouldBe(field);
    }

    private static IEnumerable<string> TextsOf(CapturedLogEntry entry) =>
        entry.State.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Append(entry.Message)
            .OfType<string>();
}
