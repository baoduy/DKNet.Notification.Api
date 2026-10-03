using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.EmailDeliverySteps;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.TeamsScenario;
using Reply = DKNet.Notification.App.BDDTests.Support.RecordingHttpStub.Reply;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The Given and When steps of <c>TeamsChannel.feature</c> (DRK-2035 §5): the service and its Teams settings, the test
/// Teams templates, what the webhook answers, and the calls. Every expected value is a literal from the spec. The
/// Then steps are in <see cref="TeamsCheckSteps" /> (the checks of a call, skips and start-up) and
/// <see cref="TeamsDeliverySteps" /> (the post, the card, attempts and logs).
/// </summary>
/// <remarks>
/// A Given step proves its set-up landed by reading the running service's settings, never through a Teams type of
/// the service: the scenarios that hold today (the health check, the whatsapp skip) share these steps.
/// </remarks>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class TeamsChannelSteps(SendScenario scenario, TeamsScenario teams)
{
    // Past the Teams time limit, so an answer this late comes after the attempt ended.
    private static readonly TimeSpan PastTheTimeLimit = TimeSpan.FromSeconds(TimeoutSeconds + 4);

    [BeforeScenario]
    public async Task BeforeScenario() => await teams.StartStubsAsync();

    [AfterScenario]
    public async Task AfterScenario() => await teams.DisposeStubsAsync();

    #region Given — the service

    [Given(@"^the service runs with sign-in on and Teams on, with the destination ""([^""]*)"" pointing at the webhook stub$")]
    public async Task GivenTheServiceRunsWithTeamsOnWithTheDestination(string destination) =>
        await StartWithTeamsAndEmailAsync(teams.TeamsAndEmailSettings(destination), destination);

    [Given(@"^the service runs with sign-in on and Teams on, with the destination ""([^""]*)"" pointing at the webhook stub, and the delivery waits of (\d+) seconds and (\d+) seconds$")]
    public async Task GivenTheServiceRunsWithTeamsOnAndTheDeliveryWaits(string destination, int first, int second)
    {
        var settings = teams.TeamsAndEmailSettings(destination);
        settings["Notifications:Delivery:RetryDelaysSeconds:0"] = Text(first);
        settings["Notifications:Delivery:RetryDelaysSeconds:1"] = Text(second);
        await StartWithTeamsAndEmailAsync(settings, destination);
    }

    [Given(@"^the service runs with sign-in on and tracing on, and Teams on with the destination ""([^""]*)"" pointing at the webhook stub with the signature ""([^""]*)""$")]
    public async Task GivenTheServiceRunsWithTracingOnAndTheSignature(string destination, string signature)
    {
        var settings = teams.TeamsAndEmailSettings(destination);
        settings[$"{Teams}:Destinations:{destination}:WebhookUrl"] = teams.WebhookUrl(signature);
        await StartWithTeamsAndEmailAsync(settings, destination);

        // The signature is in play: the destination's URL carries it, and the stub's path does not.
        teams.Configuration()[$"{Teams}:Destinations:{destination}:WebhookUrl"].ShouldEndWith($"&sig={signature}");
        PathOfWebhook.ShouldNotContain(signature);
        PathOfWebhook.ShouldNotContain(destination);
        teams.Traces.ShouldNotBeNull();
    }

    [Given(@"^the service runs with sign-in on and (the released settings|Teams on, with ""ops-alerts"" set, and a Teams template|Teams on with no destination, and a Teams template)$")]
    public async Task GivenTheServiceRunsWithSignInOnAnd(string setUp)
    {
        switch (setUp)
        {
            case "the released settings":
                // No setting at all: the released base settings decide.
                await teams.StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal));
                teams.Configuration()[$"{Teams}:Enabled"].ShouldNotBe("true");
                break;
            case "Teams on, with \"ops-alerts\" set, and a Teams template":
                await teams.StartAsync(teams.TeamsSettings("ops-alerts"));
                teams.ShouldHaveTeamsOn("ops-alerts");
                teams.AddTeamsTemplate("staff-account-opened", DoneMeansTitle, DoneMeansBody);
                break;
            default:
                await teams.StartAsync(teams.TeamsSettings());
                teams.ShouldHaveTeamsOn();
                teams.Configuration().GetSection($"{Teams}:Destinations").GetChildren().ShouldBeEmpty();
                teams.AddTeamsTemplate("staff-account-opened", DoneMeansTitle, DoneMeansBody);
                break;
        }
    }

    [Given(@"^email is off$")]
    public async Task GivenEmailIsOff()
    {
        var settings = new Dictionary<string, string?>(teams.Settings, StringComparer.Ordinal);
        foreach (var key in settings.Keys.Where(k => k.StartsWith("Notifications:Email:", StringComparison.Ordinal)).ToArray())
        {
            settings.Remove(key);
        }

        await RestartKeepingTemplatesAsync(settings);
        teams.Configuration()["Notifications:Email:Enabled"].ShouldNotBe("true");
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderStartedEvent).ShouldBeEmpty();
    }

    [Given(@"^email is set up to send to the mail catcher$")]
    public void GivenEmailIsSetUpToSendToTheMailCatcher() => teams.ShouldHaveStartedTheEmailSender();

    [Given(@"^the delivery queue has (\d+) place, and email is set up to send to the mail catcher$")]
    public async Task GivenTheDeliveryQueueHasPlacesAndEmailIsSetUp(int places)
    {
        var settings = new Dictionary<string, string?>(teams.Settings, StringComparer.Ordinal)
        {
            ["Notifications:Delivery:QueueCapacity"] = Text(places)
        };
        await RestartKeepingTemplatesAsync(settings);
        teams.ShouldHaveStartedTheEmailSender();
        teams.Configuration()["Notifications:Delivery:QueueCapacity"].ShouldBe(Text(places));
    }

    [Given(@"^the Teams time limit is (\d+) seconds and the email time limit is (\d+) seconds$")]
    public async Task GivenTheTeamsTimeLimitAndTheEmailTimeLimit(int teamsSeconds, int emailSeconds)
    {
        var settings = new Dictionary<string, string?>(teams.Settings, StringComparer.Ordinal)
        {
            [$"{Teams}:TimeoutSeconds"] = Text(teamsSeconds),
            ["Notifications:Email:TimeoutSeconds"] = Text(emailSeconds)
        };
        await RestartKeepingTemplatesAsync(settings);
        teams.ShouldHaveStartedTheEmailSender();
    }

    [Given(@"^the destination ""([^""]*)"" answers with a certificate the service does not trust$")]
    public async Task GivenTheDestinationAnswersWithACertificateTheServiceDoesNotTrust(string destination)
    {
        await teams.StartUntrustedStubAsync();
        var settings = new Dictionary<string, string?>(teams.Settings, StringComparer.Ordinal)
        {
            [$"{Teams}:Destinations:{destination}:WebhookUrl"] = teams.WebhookUrl()
        };
        await RestartKeepingTemplatesAsync(settings);
        teams.ShouldHaveTeamsOn(destination);
    }

    [Given(@"^the service started with sign-in on, Teams on, the destination ""([^""]*)"" pointing at the webhook stub, and the destination ""([^""]*)"" with (.+)$")]
    public async Task GivenTheServiceStartedWithABadDestination(string good, string bad, string fault)
    {
        var settings = teams.TeamsSettings(good);
        var key = $"{Teams}:Destinations:{bad}";
        switch (fault)
        {
            case "no webhook URL":
                // The name is set with no URL, as a base settings file would set it with no secret source behind it.
                settings[key] = string.Empty;
                break;
            case "an empty webhook URL":
                settings[$"{key}:WebhookUrl"] = string.Empty;
                break;
            case "the webhook URL \"http://127.0.0.1/hook\"":
                settings[$"{key}:WebhookUrl"] = "http://127.0.0.1/hook";
                break;
            case "the webhook URL \"hooks/finance\"":
                settings[$"{key}:WebhookUrl"] = "hooks/finance";
                break;
            case "a webhook URL of 2,049 characters":
                // At the webhook stub, so a post sent to it would show.
                settings[$"{key}:WebhookUrl"] = WebhookUrlOfLength(2_049);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "no such destination fault in the spec");
        }

        await teams.StartAsync(settings);
        teams.ShouldHaveTeamsOn(good);
    }

    [Given(@"^the service started with sign-in on, Teams on, and the destination ""([^""]*)"" pointing at the webhook stub$")]
    public async Task GivenTheServiceStartedWithTheDestination(string destination)
    {
        await teams.StartAsync(teams.TeamsSettings(destination));
        // The name went in with its case: the settings' keys are case-blind, the destination names are not.
        teams.Configuration().GetSection($"{Teams}:Destinations").GetChildren().ShouldHaveSingleItem().Key.ShouldBe(destination);
    }

    [Given(@"^the service started with sign-in on, email set up to send to the mail catcher, Teams settings with the destination ""([^""]*)"", and (.+)$")]
    public async Task GivenTheServiceStartedWithABadTeamsSetting(string destination, string fault)
    {
        var settings = teams.TeamsAndEmailSettings(destination);
        switch (fault)
        {
            case "Teams on and a time limit of 0 seconds":
                settings[$"{Teams}:TimeoutSeconds"] = "0";
                break;
            case "Teams on and a time limit of 121 seconds":
                settings[$"{Teams}:TimeoutSeconds"] = "121";
                break;
            case "the Teams on/off value \"yes\"":
                settings[$"{Teams}:Enabled"] = "yes";
                break;
            case "Teams on and 101 destinations":
                // "ops-alerts" and 100 more, each good on its own: only the count breaks the rule.
                for (var i = 1; i <= 100; i++)
                {
                    settings[$"{Teams}:Destinations:team-{i:000}:WebhookUrl"] = teams.WebhookUrl();
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "no such Teams setting fault in the spec");
        }

        await teams.StartAsync(settings);
        // The service started, and email with it.
        teams.ShouldHaveStartedTheEmailSender();
        if (fault == "Teams on and 101 destinations")
        {
            teams.Configuration().GetSection($"{Teams}:Destinations").GetChildren().Count().ShouldBe(101);
        }
    }

    [Given(@"^the service started with Teams on and no destination$")]
    public async Task GivenTheServiceStartedWithTeamsOnAndNoDestination()
    {
        await teams.StartAsync(teams.TeamsSettings());
        teams.ShouldHaveTeamsOn();
        teams.Configuration().GetSection($"{Teams}:Destinations").GetChildren().ShouldBeEmpty();
    }

    [Given(@"^the destination ""([^""]*)"" is added while the service runs$")]
    public void GivenTheDestinationIsAddedWhileTheServiceRuns(string destination)
    {
        var key = $"{Teams}:Destinations:{destination}:WebhookUrl";
        scenario.Factory.Settings.Change(key, teams.WebhookUrl());

        // The change landed: the running service's settings now hold the destination.
        teams.Configuration()[key].ShouldBe(teams.WebhookUrl());
    }

    [Given(@"^the webhook stub is stopped$")]
    public async Task GivenTheWebhookStubIsStopped() => await teams.WebhookStub.StopAsync();

    [Given(@"^""([^""]*)"" is a caller allowed to send notifications$")]
    public void GivenIsACallerAllowedToSendNotifications(string caller) => scenario.AllowCaller(caller);

    #endregion

    #region Given — the templates

    [Given(@"^the template ""([^""]*)"" has a Teams version with (no title|the title ""[^""]*"") and the body ""([^""]*)""$")]
    public void GivenTheTemplateHasATeamsVersionWith(string templateId, string title, string body) =>
        teams.AddTeamsTemplate(templateId, title == "no title" ? null : title["the title \"".Length..^1], body);

    [Given(@"^the template ""([^""]*)"" has a Teams version$")]
    public void GivenTheTemplateHasATeamsVersion(string templateId) =>
        teams.AddTeamsTemplate(templateId, DoneMeansTitle, DoneMeansBody);

    [Given(@"^the template ""([^""]*)"" has a Teams version whose body holds the (\d+) tokens ""\{\{part1\}\}"" to ""\{\{part(\d+)\}\}""$")]
    public void GivenTheTemplateHasATeamsVersionWhoseBodyHoldsTheTokens(string templateId, int count, int last)
    {
        last.ShouldBe(count);
        // The tokens only, with no title: every byte of the posted body is a value.
        teams.AddTeamsTemplate(templateId, title: null, string.Concat(Enumerable.Range(1, count).Select(i => $"{{{{part{i}}}}}")));
    }

    #endregion

    #region Given — what the webhook answers

    [Given(@"^the webhook answers the post with HTTP (\d+)$")]
    public void GivenTheWebhookAnswersThePostWith(int status) => teams.WebhookStub.DefaultReply = Reply.Status(status);

    [Given(@"^the webhook answers attempt 1 with (\d+) and the text ""([^""]*)""$")]
    public void GivenTheWebhookAnswersAttempt1WithTheText(int status, string text)
    {
        teams.ExpectedCode = Text(status);
        teams.WebhookStub.AnswerOnce(Reply.Status(status, text));
    }

    // Not the answer-text step, which also begins "the webhook answers attempt 1 with".
    [Given(@"^the webhook answers attempt 1 with (?!\d+ and the text )(.+)$")]
    public async Task GivenTheWebhookAnswersAttempt1With(string answer)
    {
        var stub = teams.WebhookStub;
        var status = Regex.Match(answer, @"^HTTP (\d+)$");
        if (status.Success)
        {
            var code = int.Parse(status.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Script(code, Reply.Status(code));
            return;
        }

        switch (answer)
        {
            case "HTTP 429 with a retry after of 2 seconds":
                Script(429, Reply.Status(429, null, ("Retry-After", "2")));
                break;
            case "HTTP 429 with no retry-after header":
                Script(429, Reply.Status(429));
                break;
            case "a refused connection":
                await stub.PauseAsync();
                teams.ClearFault = stub.ResumeAsync;
                break;
            case "a lost connection":
                stub.AnswerOnce(Reply.Lost());
                break;
            case "a post it records but answers only after the Teams time limit":
                stub.AnswerOnce(Reply.Status(202) with { Delay = PastTheTimeLimit });
                break;
            case "HTTP 302 to \"https://elsewhere.example\"":
                // The elsewhere stub stands in for https://elsewhere.example, so a redirect followed would show.
                Script(302, Reply.Status(302, null, ("Location", teams.Elsewhere.Address.ToString())));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "no such webhook answer in the spec");
        }
    }

    [Given(@"^the webhook answers (\d+) to every post$")]
    public void GivenTheWebhookAnswersToEveryPost(int status)
    {
        teams.ExpectedCode = Text(status);
        teams.WebhookStub.DefaultReply = Reply.Status(status);
    }

    [Given(@"^the webhook answers attempt 1 only after (\d+) seconds$")]
    public void GivenTheWebhookAnswersAttempt1OnlyAfter(int seconds) =>
        teams.WebhookStub.AnswerOnce(Reply.Status(202) with { Delay = TimeSpan.FromSeconds(seconds) });

    [Given(@"^the webhook answers the first post with 429 and a retry after of (\d+) seconds$")]
    public void GivenTheWebhookAnswersTheFirstPostWith429(int seconds) =>
        Script(429, Reply.Status(429, null, ("Retry-After", Text(seconds))));

    [Given(@"^a Teams notification waits for its attempt 2 after a 429 with a retry after of (\d+) seconds$")]
    public async Task GivenATeamsNotificationWaitsForItsAttempt2(int seconds)
    {
        teams.EnsureDoneMeansTemplate("staff-account-opened");
        Script(429, Reply.Status(429, null, ("Retry-After", Text(seconds))));
        await teams.SendAsync("ops-alerts", TeamsBody("staff-account-opened", "ops-alerts", DoneMeansValues()));
        var id = teams.NotificationOf("ops-alerts");
        var failure = await teams.AttemptFailureAsync(id, 1, TimeSpan.FromSeconds(30));
        failure.Value("ReplyCode").ShouldBe("429");
    }

    #endregion

    #region When

    // The customer value may hold quotes, written \" in the step text (the scenario "A value stays text").
    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to the Teams destination ""([^""]*)"" for customer ""((?:[^""\\]|\\.)*)"" and account ""([^""]*)""$")]
    public async Task WhenPostsTemplateForCustomerAndAccount(string caller, string templateId, string destination, string customerName, string accountNumber)
    {
        caller.ShouldBe(Caller);
        customerName = Regex.Unescape(customerName);
        teams.LastCustomer = customerName;
        await teams.SendAsync(TeamsBody(templateId, destination, DoneMeansValues(customerName, accountNumber)));
    }

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to the Teams destination ""([^""]*)"" for customer ""([^""]*)""$")]
    public async Task WhenPostsTemplateForCustomer(string caller, string templateId, string destination, string customerName) =>
        await WhenPostsTemplateForCustomerAndAccount(caller, templateId, destination, customerName, AccountNumber);

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to the Teams destination ""([^""]*)""$")]
    public async Task WhenPostsTemplateToTheTeamsDestination(string caller, string templateId, string destination) =>
        await WhenPostsTemplateForCustomerAndAccount(caller, templateId, destination, CustomerName, AccountNumber);

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" on channel ""([^""]*)"" to the Teams destination ""([^""]*)""$")]
    [When(@"^""([^""]*)"" sends template ""([^""]*)"" on channel ""([^""]*)"" to the Teams destination ""([^""]*)""$")]
    public async Task WhenPostsTemplateOnChannel(string caller, string templateId, string channel, string destination)
    {
        caller.ShouldBe(Caller);
        await teams.SendAsync(TeamsBody(templateId, destination, DoneMeansValues(), channel));
    }

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" with no Teams destination$")]
    public async Task WhenPostsTemplateWithNoTeamsDestination(string caller, string templateId)
    {
        caller.ShouldBe(Caller);
        await teams.SendAsync(TeamsBody(templateId, destination: null, DoneMeansValues()));
    }

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to a Teams destination name of (\d+) characters$")]
    public async Task WhenPostsTemplateToADestinationNameOfLength(string caller, string templateId, int length) =>
        await WhenPostsTemplateToTheTeamsDestination(caller, templateId, new string('a', length));

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to the Teams destination ""([^""]*)"" for customer ""([^""]*)"" with no account$")]
    public async Task WhenPostsTemplateForCustomerWithNoAccount(string caller, string templateId, string destination, string customerName)
    {
        caller.ShouldBe(Caller);
        await teams.SendAsync(TeamsBody(templateId, destination, new JsonObject { ["customerName"] = customerName }));
    }

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to the Teams destination ""([^""]*)"" for team ""([^""]*)"" (with no other value|with headline """"|with a headline of \d+ characters)$")]
    public async Task WhenPostsTemplateForTeam(string caller, string templateId, string destination, string team, string extra)
    {
        caller.ShouldBe(Caller);
        var values = new JsonObject { ["team"] = team };
        if (extra == "with headline \"\"")
        {
            values["headline"] = string.Empty;
        }
        else if (extra != "with no other value")
        {
            var length = Regex.Match(extra, @"^with a headline of (\d+) characters$").Groups[1].Value;
            values["headline"] = Headline(int.Parse(length, System.Globalization.CultureInfo.InvariantCulture));
        }

        await teams.SendAsync(TeamsBody(templateId, destination, values));
    }

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to the Teams destination ""([^""]*)"" with plain-text values that make the posted message ([\d,]+) bytes$")]
    public async Task WhenPostsTemplateWithValuesThatMakeThePostedMessage(string caller, string templateId, string destination, string size)
    {
        caller.ShouldBe(Caller);
        var bytes = int.Parse(size, System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture);
        await teams.SendAsync(TeamsBody(templateId, destination, PartsOfMessage(bytes)));
    }

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)""$")]
    public async Task WhenEmailsTemplateTo(string caller, string templateId, string to)
    {
        caller.ShouldBe(Caller);
        await teams.SendAsync(SendScenario.Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = CustomerName,
            ["accountNumber"] = AccountNumber
        }));
    }

    [When(@"^""([^""]*)"" posts a card to ""([^""]*)"" for customer ""([^""]*)"" and then emails ""([^""]*)""$")]
    public async Task WhenPostsACardForCustomerAndThenEmails(string caller, string destination, string customerName, string to)
    {
        caller.ShouldBe(Caller);
        teams.EnsureDoneMeansTemplate("staff-account-opened");
        // In a row: the email goes once the card's attempt 1 has ended.
        await teams.SendAsync(customerName, TeamsBody("staff-account-opened", destination, DoneMeansValues(customerName)));
        var cardId = teams.NotificationOf(customerName);
        await TeamsScenario.EventuallyAsync(
            () => Task.FromResult<object?>(teams.EntriesFor(AttemptFailedEvent, cardId).Concat(teams.EntriesFor(DeliveredEvent, cardId)).FirstOrDefault()),
            TimeSpan.FromSeconds(30),
            $"attempt 1 of the card for {customerName} did not end");
        await teams.SendAsync(to, EmailBody(to));
    }

    [When(@"^""([^""]*)"" posts template ""([^""]*)"" to ""([^""]*)"" and then to ""([^""]*)""$")]
    public async Task WhenPostsTemplateToAndThenTo(string caller, string templateId, string first, string second)
    {
        caller.ShouldBe(Caller);
        await teams.SendAsync(first, TeamsBody(templateId, first, DoneMeansValues()));
        await teams.SendAsync(second, TeamsBody(templateId, second, DoneMeansValues()));
    }

    [When(@"^""([^""]*)"" posts a card to ""([^""]*)"" and emails ""([^""]*)""$")]
    public async Task WhenPostsACardAndEmails(string caller, string destination, string to)
    {
        caller.ShouldBe(Caller);
        teams.EnsureDoneMeansTemplate("staff-account-opened");
        await teams.SendAsync(destination, TeamsBody("staff-account-opened", destination, DoneMeansValues()));
        await teams.SendAsync(to, EmailBody(to));
    }

    [When(@"^""([^""]*)"" posts a card to ""([^""]*)""$")]
    public async Task WhenPostsACardTo(string caller, string destination)
    {
        caller.ShouldBe(Caller);
        // "A card" is the template of §1 "Done means": the scenario names no template of its own.
        teams.EnsureDoneMeansTemplate("staff-account-opened");
        await teams.SendAsync(TeamsBody("staff-account-opened", destination, DoneMeansValues()));
    }

    [When(@"^the cluster's liveness probe asks the health check without a token$")]
    public async Task WhenTheLivenessProbeAsksTheHealthCheckWithoutAToken() =>
        teams.ProbeAnswer = await scenario.GetAsync("/healthz");

    #endregion

    #region Helpers

    private void Script(int status, Reply reply)
    {
        teams.ExpectedCode = Text(status);
        teams.WebhookStub.AnswerOnce(reply);
    }

    private async Task StartWithTeamsAndEmailAsync(IReadOnlyDictionary<string, string?> settings, string destination)
    {
        await teams.StartAsync(settings);
        teams.ShouldHaveTeamsOn(destination);
        // Email is set up too, so "the mail catcher receives nothing" can fail.
        teams.ShouldHaveStartedTheEmailSender();
    }

    /// <summary>A restart, as a settings change at the next start: the scenario's test templates go with it.</summary>
    private async Task RestartKeepingTemplatesAsync(IReadOnlyDictionary<string, string?> settings)
    {
        var added = scenario.Factory.Gate.Templates.Where(t => t.Versions.Any(v => v.Channel == "teams")).ToArray();
        await teams.StartAsync(settings);
        foreach (var template in added)
        {
            scenario.Factory.Gate.Add(template);
        }
    }

    private string WebhookUrlOfLength(int length)
    {
        var url = teams.WebhookUrl();
        var path = PathOfWebhook + "/" + new string('f', length - url.Length - 1);
        url = teams.WebhookUrl(path: path);
        url.Length.ShouldBe(length);
        return url;
    }

    /// <summary>A headline of <paramref name="length" /> characters whose last one differs, so a cut shows.</summary>
    private static string Headline(int length) => new string('h', length - 1) + "Z";

    /// <summary>
    /// 8 plain-text values (ASCII letters, which the serializer writes as they are) whose bytes, added to the
    /// <see cref="EmptyMessage" /> of brief DRK-2036 §5, make a posted message of exactly <paramref name="bytes" /> bytes.
    /// </summary>
    private static JsonObject PartsOfMessage(int bytes)
    {
        const int parts = 8;
        var valueBytes = bytes - Encoding.UTF8.GetByteCount(EmptyMessage);
        var values = new JsonObject();
        for (var i = 0; i < parts; i++)
        {
            var length = (valueBytes / parts) + (i < valueBytes % parts ? 1 : 0);
            // Each value inside the 4,000-character parameter rule, so only the message size is at stake.
            length.ShouldBeLessThanOrEqualTo(4_000);
            values[$"part{i + 1}"] = new string((char)('a' + i), length);
        }

        values.Sum(v => v.Value!.GetValue<string>().Length).ShouldBe(valueBytes);
        return values;
    }

    private static string EmailBody(string to) =>
        SendScenario.Body("email", "account-opened", new JsonObject
        {
            ["to"] = to,
            ["customerName"] = CustomerName,
            ["accountNumber"] = AccountNumber
        });

    #endregion
}
