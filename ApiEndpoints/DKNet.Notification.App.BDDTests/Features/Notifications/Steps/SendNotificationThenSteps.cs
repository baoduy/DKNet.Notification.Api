using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>Then steps for <c>SendNotification.feature</c> (DRK-2013 §5). Every expected value is a literal from the spec.</summary>
[Binding]
[Scope(Feature = SendNotificationSteps.FeatureTitle)]
public sealed class SendNotificationThenSteps(SendScenario scenario)
{
    #region Accepted

    [Then(@"^the call is accepted with a new notification id$")]
    [Then(@"^the second call is accepted with a new notification id$")]
    public void ThenTheCallIsAcceptedWithANewNotificationId() => scenario.ShouldBeAccepted(scenario.LastAnswer);

    [Then(@"^the second answer carries the same notification id as the first$")]
    public void ThenTheSecondAnswerCarriesTheSameNotificationIdAsTheFirst()
    {
        scenario.Answers.Count.ShouldBe(2);
        scenario.Answers.ShouldAllBe(answer => answer.Status == HttpStatusCode.Accepted);
        NotificationIdOf(scenario.Answers[1]).ShouldBe(NotificationIdOf(scenario.Answers[0]));
    }

    [Then(@"^""([^""]*)"" gets a different notification id from ""([^""]*)""$")]
    public void ThenGetsADifferentNotificationIdFrom(string caller, string otherCaller)
    {
        var id = NotificationIdOf(scenario.Answers.Last(a => a.Caller == caller).ShouldNotBeNull());
        var otherId = NotificationIdOf(scenario.Answers.Last(a => a.Caller == otherCaller).ShouldNotBeNull());
        id.ShouldNotBe(otherId);
    }

    #endregion

    #region Skip warnings

    [Then(@"^exactly 1 skip warning is logged with reason ""([^""]*)"" and caller ""([^""]*)""$")]
    public void ThenExactly1SkipWarningIsLoggedWithReasonAndCaller(string reason, string caller)
    {
        var entry = scenario.SingleSkipEntry();
        ShouldBeSkipEntry(entry, NotificationIdOf(scenario.LastAnswer), "account-opened", "email", caller);
        entry.Value("Reason").ShouldBe(reason);
    }

    [Then(@"^the skip warning names channel ""([^""]*)"" and reason ""([^""]*)""$")]
    public void ThenTheSkipWarningNamesChannelAndReason(string channel, string reason)
    {
        var entry = scenario.SingleSkipEntry();
        entry.Value("Channel").ShouldBe(channel);
        entry.Value("Reason").ShouldBe(reason);
    }

    [Then(@"^the skip warning names reason ""([^""]*)""$")]
    public void ThenTheSkipWarningNamesReason(string reason) =>
        scenario.SingleSkipEntry().Value("Reason").ShouldBe(reason);

    [Then(@"^the skip warning names caller ""([^""]*)""$")]
    public void ThenTheSkipWarningNamesCaller(string caller) =>
        scenario.SingleSkipEntry().Value("CallerId").ShouldBe(caller);

    [Then(@"^exactly 1 skip warning is logged$")]
    public void ThenExactly1SkipWarningIsLogged() =>
        ShouldBeSkipEntry(
            scenario.SkipEntries.ShouldHaveSingleItem(),
            NotificationIdOf(scenario.Answers[0]),
            "account-opened",
            "email",
            "treasury-ops");

    [Then(@"^exactly 1 skip warning is logged, and the accepted count rose by 1$")]
    public void ThenExactly1SkipWarningIsLoggedAndTheAcceptedCountRoseBy1()
    {
        ThenExactly1SkipWarningIsLogged();
        scenario.Metrics.Sum(AcceptedCounter, ("channel", "email"), ("outcome", "skipped")).ShouldBe(1);
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(1);
    }

    [Then(@"^2 skip warnings are logged$")]
    public void Then2SkipWarningsAreLogged()
    {
        var entries = scenario.SkipEntries;
        entries.Count.ShouldBe(2);
        foreach (var caller in new[] { "treasury-ops", "card-ops" })
        {
            var answer = scenario.Answers.Last(a => a.Caller == caller);
            ShouldBeSkipEntry(
                entries.Single(e => e.Value("CallerId") == caller),
                NotificationIdOf(answer),
                "account-opened",
                "email",
                caller);
        }
    }

    [Then(@"^no skip warning is logged$")]
    public void ThenNoSkipWarningIsLogged()
    {
        scenario.SkipEntries.ShouldBeEmpty();
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(0);
    }

    [Then(@"^no log entry holds ""([^""]*)"", ""([^""]*)"" or ""([^""]*)""$")]
    public void ThenNoLogEntryHolds(string first, string second, string third)
    {
        // The call was handled and logged, so the search below runs over a real skip entry.
        scenario.ShouldBeAccepted(scenario.LastAnswer);
        scenario.SingleSkipEntry();

        var logged = scenario.AllLoggedText();
        foreach (var personalData in new[] { first, second, third })
        {
            logged.ShouldAllBe(text => !text.Contains(personalData, StringComparison.OrdinalIgnoreCase));
        }
    }

    #endregion

    #region Refused

    [Then(@"^the call is refused with status (\d+) and an empty body$")]
    public void ThenTheCallIsRefusedWithStatusAndAnEmptyBody(int status)
    {
        ((int)scenario.LastAnswer.Status).ShouldBe(status);
        scenario.LastAnswer.Body.ShouldBe(string.Empty);
    }

    [Then(@"^the call is refused with status (\d+)$")]
    [Then(@"^the second call is refused with status (\d+)$")]
    public void ThenTheCallIsRefusedWithStatus(int status) =>
        ((int)scenario.LastAnswer.Status).ShouldBe(status, scenario.LastAnswer.Body);

    [Then(@"^the call is refused with ""([^""]*)"" and a trace id$")]
    [Then(@"^the call is refused with ""([^""]*)""$")]
    public void ThenTheCallIsRefusedWith(string code) => ShouldBeRefusedWith(scenario.LastAnswer, code);

    [Then(@"^1 rejection entry is logged with ""([^""]*)"", and the rejected count for it rose by 1$")]
    public void Then1RejectionEntryIsLoggedAndTheRejectedCountRoseBy1(string code)
    {
        scenario.SingleRejectionEntry(code, ShouldBeRefusedWith(scenario.LastAnswer, code), "treasury-ops");
        scenario.Metrics.Sum(RejectedCounter, ("code", code)).ShouldBe(1);
        scenario.Metrics.Sum(RejectedCounter).ShouldBe(1);
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(0);
    }

    [Then(@"^1 rejection entry is logged with ""([^""]*)"" and template ""([^""]*)""$")]
    public void Then1RejectionEntryIsLoggedWithAndTemplate(string code, string templateId)
    {
        var entry = scenario.SingleRejectionEntry(code, ShouldBeRefusedWith(scenario.LastAnswer, code), "treasury-ops");
        entry.Value("TemplateId").ShouldBe(templateId);
        entry.Value("Channel").ShouldBe("email");
        scenario.SkipEntries.ShouldBeEmpty();
    }

    #endregion
}
