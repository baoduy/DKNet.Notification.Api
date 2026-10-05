using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// Steps for <c>EmailChannel.feature</c> (DRK-2020 §5, surface A). Every expected value is a literal from the spec;
/// the setting names are the contract names of brief DRK-2025 §5. Scoped to the feature, so its step texts never
/// clash with the slice 2 send feature's.
/// </summary>
/// <remarks>
/// The start-up entries are matched on the event names the spec's log table gives, with the structured-state names
/// this file fixes: <c>EmailSenderStarted</c> (Information, <c>Sender</c>) and <c>EmailSenderNotConfigured</c>
/// (Warning, <c>Settings</c>: the bad setting names, separated by <c>", "</c>). The queue length is the gauge
/// <c>notifications.queue.length</c>.
/// </remarks>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class EmailChannelSteps(SendScenario scenario)
{
    public const string FeatureTitle = "Email channel with the SMTP sender, rendering and delivery";
    public const string SenderStartedEvent = "EmailSenderStarted";
    public const string SenderNotConfiguredEvent = "EmailSenderNotConfigured";
    public const string QueueLengthGauge = "notifications.queue.length";

    // The spec's 2 sample values (§1 "Done means"), so a call that reaches rendering fills every released token.
    private const string CustomerName = "Jane Tan";
    private const string AccountNumber = "0012345678";

    private MailCatcher? _mailCatcher;
    private IReadOnlyDictionary<string, string?>? _startSettings;
    private string[] _forbiddenValues = [];

    private MailCatcher MailCatcher => _mailCatcher.ShouldNotBeNull();

    [BeforeScenario]
    public async Task BeforeScenario()
    {
        _mailCatcher = await MailCatcher.SharedAsync();
        await _mailCatcher.EnsureRunningAsync();
        await _mailCatcher.ClearAsync();
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        await scenario.DisposeAsync();
        if (_mailCatcher is not null)
        {
            await _mailCatcher.EnsureRunningAsync();
        }
    }

    #region Given — the service

    [Given(@"^the service runs with sign-in on and email off$")]
    [Given(@"^the service started with email off$")]
    public async Task GivenTheServiceRunsWithEmailOff() => await StartAsync(settings: null);

    [Given(@"^the service runs with sign-in on and email on with the sender ""([^""]*)""$")]
    public async Task GivenTheServiceRunsWithEmailOnWithTheSender(string sender) =>
        await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Notifications:Email:Enabled"] = "true",
            ["Notifications:Email:Sender"] = sender
        });

    [Given(@"^the service runs with sign-in on and email set up to the mail catcher, and a template ""([^""]*)"" with only a Teams version$")]
    public async Task GivenTheServiceRunsWithATemplateWithOnlyATeamsVersion(string templateId)
    {
        // Index 90 keeps the entry clear of the released registrations. The loader only needs the file to exist,
        // so the Teams version points at the released file; no test template is added to the release.
        var settings = new Dictionary<string, string?>(MailCatcher.EmailSettings(), StringComparer.Ordinal)
        {
            ["Notifications:Templates:90:TemplateId"] = templateId,
            ["Notifications:Templates:90:Versions:0:Channel"] = "teams",
            ["Notifications:Templates:90:Versions:0:File"] = "account-opened.email.html",
            ["Notifications:Templates:90:Versions:0:Format"] = "Markdown"
        };
        await StartAsync(settings);
        ShouldHaveStartedTheEmailSender();
    }

    [Given(@"^the service runs with sign-in on and email set up to send to the mail catcher$")]
    [Given(@"^the service runs with its released template catalogue, sign-in on and email set up to send to the mail catcher$")]
    public async Task GivenTheServiceRunsWithEmailSetUp()
    {
        await StartAsync(MailCatcher.EmailSettings());
        ShouldHaveStartedTheEmailSender();
    }

    [Given(@"^the service runs with sign-in on, email set up to send to the mail catcher and a queue size of (\d+)$")]
    public async Task GivenTheServiceRunsWithAQueueSizeOf(int queueSize)
    {
        await StartAsync(new Dictionary<string, string?>(MailCatcher.EmailSettings(), StringComparer.Ordinal)
        {
            ["Notifications:Delivery:QueueCapacity"] = queueSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        ShouldHaveStartedTheEmailSender();
    }

    [Given(@"^email is on, with the sender ""([^""]*)"" and every SMTP setting given$")]
    public void GivenEmailIsOnWithTheSenderAndEverySmtpSettingGiven(string sender) =>
        _startSettings = new Dictionary<string, string?>(MailCatcher.EmailSettings(), StringComparer.Ordinal)
        {
            ["Notifications:Email:Sender"] = sender
        };

    [Given(@"^email is off$")]
    public void GivenEmailIsOff() =>
        // No email setting at all: the released base settings must keep email off on their own.
        _startSettings = new Dictionary<string, string?>(StringComparer.Ordinal);

    [Given(@"^the service started with email on and (.+)$")]
    public async Task GivenTheServiceStartedWithEmailOnAnd(string fault)
    {
        var settings = new Dictionary<string, string?>(MailCatcher.EmailSettings(), StringComparer.Ordinal);
        string? faultValue = null;
        switch (fault)
        {
            case "no mail server host":
                settings.Remove("Notifications:Email:Smtp:Host");
                break;
            case "no sender address":
                settings.Remove("Notifications:Email:Smtp:FromAddress");
                break;
            case "the sender address \"notify.example.com\"":
                faultValue = settings["Notifications:Email:Smtp:FromAddress"] = "notify.example.com";
                break;
            case "the port 70000":
                faultValue = settings["Notifications:Email:Smtp:Port"] = "70000";
                break;
            case "the security mode \"None\"":
                faultValue = settings["Notifications:Email:Smtp:Security"] = "None";
                break;
            case "a time limit of 500 seconds":
                faultValue = settings["Notifications:Email:TimeoutSeconds"] = "500";
                break;
            case "the sender \"SendGrid\"":
                faultValue = settings["Notifications:Email:Sender"] = "SendGrid";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "no such fault in the spec");
        }

        // Every value the warning must not hold. "Smtp" and "true" are left out: they are part of setting names
        // and of ordinary text, not values only a leak would show.
        _forbiddenValues = new[]
            {
                faultValue,
                settings.GetValueOrDefault("Notifications:Email:Smtp:Host"),
                settings.GetValueOrDefault("Notifications:Email:Smtp:Port"),
                settings.GetValueOrDefault("Notifications:Email:Smtp:FromAddress"),
                settings.GetValueOrDefault("Notifications:Email:Smtp:FromName")
            }
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await StartAsync(settings);
    }

    [Given(@"^the email settings are turned on while the service runs$")]
    public void GivenTheEmailSettingsAreTurnedOnWhileTheServiceRuns()
    {
        var configuration = scenario.Factory.Services.GetRequiredService<IConfiguration>();
        foreach (var (key, value) in MailCatcher.EmailSettings())
        {
            scenario.Factory.Settings.Change(key, value);
        }

        // The change landed: the running service's settings now set email up.
        configuration["Notifications:Email:Enabled"].ShouldBe("true");
        configuration["Notifications:Email:Smtp:Host"].ShouldBe(MailCatcher.Host);
    }

    #endregion

    #region Given — callers and earlier calls

    [Given(@"^""([^""]*)"" is a caller allowed to send notifications$")]
    public void GivenIsACallerAllowedToSendNotifications(string caller) => scenario.AllowCaller(caller);

    [Given(@"^(\d+) notifications of ""([^""]*)"" are not delivered yet, because the mail catcher is stopped$")]
    public async Task GivenNotificationsAreNotDeliveredYetBecauseTheMailCatcherIsStopped(int count, string caller)
    {
        await MailCatcher.StopAsync();
        for (var i = 0; i < count; i++)
        {
            scenario.ShouldBeAccepted(await scenario.SendAsync(caller, NewKey(), ValidEmailBody("account-opened", "jane@example.com")));
        }
    }

    #endregion

    #region When

    [When(@"^the service starts$")]
    public async Task WhenTheServiceStarts() =>
        await StartAsync(_startSettings.ShouldNotBeNull("a Given step must choose the email settings first"));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" with no recipient$")]
    public async Task WhenEmailsTemplateWithNoRecipient(string caller, string templateId) =>
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, new JsonObject()));

    [When(@"^""([^""]*)"" sends template ""([^""]*)"" to ""([^""]*)"" on channel ""([^""]*)""$")]
    public async Task WhenSendsTemplateToOnChannel(string caller, string templateId, string to, string channel) =>
        await scenario.SendAsync(caller, NewKey(), Body(channel, templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = CustomerName,
            ["accountNumber"] = AccountNumber
        }));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to (.+) for customer ""([^""]*)"" and account ""([^""]*)""$")]
    public async Task WhenEmailsTemplateToForCustomerAndAccount(
        string caller,
        string templateId,
        string recipient,
        string customerName,
        string accountNumber)
    {
        var parameters = new JsonObject();
        var to = Recipient(recipient);
        if (to is not null)
        {
            parameters["to"] = to;
        }

        parameters["customerName"] = customerName;
        parameters["accountNumber"] = accountNumber;
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, parameters));
    }

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)"" for customer ""([^""]*)"" with no account$")]
    public async Task WhenEmailsTemplateToForCustomerWithNoAccount(string caller, string templateId, string to, string customerName) =>
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = customerName
        }));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)""$")]
    public async Task WhenEmailsTemplateTo(string caller, string templateId, string to) =>
        await scenario.SendAsync(caller, NewKey(), ValidEmailBody(templateId, to));

    #endregion

    #region Then — accepted and skipped

    [Then(@"^the call is accepted with a new notification id$")]
    public void ThenTheCallIsAcceptedWithANewNotificationId() => scenario.ShouldBeAccepted(scenario.LastAnswer);

    [Then(@"^the skip warning names reason ""([^""]*)"", and the accepted count for ""([^""]*)"" with outcome ""([^""]*)"" rose by 1$")]
    public void ThenTheSkipWarningNamesReasonAndTheAcceptedCountRoseBy1(string reason, string channel, string outcome)
    {
        var entry = scenario.SingleSkipEntry();
        entry.Value("Reason").ShouldBe(reason);
        entry.Value("Channel").ShouldBe(channel);
        entry.Value("CallerId").ShouldBe("treasury-ops");
        scenario.Metrics.Sum(AcceptedCounter, ("channel", channel), ("outcome", outcome)).ShouldBe(1);
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(1);
    }

    [Then(@"^the skip warning names reason ""([^""]*)""$")]
    public void ThenTheSkipWarningNamesReason(string reason) =>
        scenario.SingleSkipEntry().Value("Reason").ShouldBe(reason);

    [Then(@"^the call is accepted and skipped with reason ""([^""]*)""$")]
    public void ThenTheCallIsAcceptedAndSkippedWithReason(string reason)
    {
        scenario.ShouldBeAccepted(scenario.LastAnswer);
        var entry = scenario.SingleSkipEntry();
        entry.Value("Reason").ShouldBe(reason);
        entry.Value("Channel").ShouldBe("email");
        scenario.Metrics.Sum(AcceptedCounter, ("channel", "email"), ("outcome", "skipped")).ShouldBe(1);
    }

    [Then(@"^the mail catcher receives no mail$")]
    public async Task ThenTheMailCatcherReceivesNoMail()
    {
        // A short grace, so a mail sent just after the answer would still show.
        await Task.Delay(TimeSpan.FromSeconds(1));
        (await MailCatcher.MailCountAsync()).ShouldBe(0);
    }

    #endregion

    #region Then — refused

    [Then(@"^the call is refused with ""([^""]*)"" for the recipient, and a trace id$")]
    public void ThenTheCallIsRefusedForTheRecipient(string code)
    {
        ShouldBeRefusedWith(scenario.LastAnswer, code);
        ShouldNameTheField(scenario.LastAnswer, code, "to");
    }

    [Then(@"^the call is refused with ""([^""]*)"" naming ""([^""]*)""$")]
    public void ThenTheCallIsRefusedNaming(string code, string parameter)
    {
        ShouldBeRefusedWith(scenario.LastAnswer, code);
        ShouldNameTheField(scenario.LastAnswer, code, $"parameters.{parameter}");
    }

    [Then(@"^1 rejection entry is logged with ""([^""]*)"", and the rejected count for ""([^""]*)"" rose by 1$")]
    public void Then1RejectionEntryIsLoggedAndTheRejectedCountRoseBy1(string code, string countedCode)
    {
        ShouldBeLoggedAndCounted(code, countedCode, ShouldBeRefusedWith(scenario.LastAnswer, code));
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(0);
    }

    [Then(@"^the call is refused with ""([^""]*)"", status (\d+) and a retry after (\d+) seconds$")]
    public void ThenTheCallIsRefusedWithStatusAndARetryAfter(string code, int status, int seconds)
    {
        ShouldBeRefusedWith(scenario.LastAnswer, (HttpStatusCode)status, code);
        scenario.LastAnswer.RetryAfter.ShouldBe(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Then(@"^1 rejection entry is logged with ""([^""]*)"", the rejected count for ""([^""]*)"" rose by 1, and the queue length shows (\d+)$")]
    public void Then1RejectionEntryIsLoggedAndTheQueueLengthShows(string code, string countedCode, int queueLength)
    {
        ShouldBeLoggedAndCounted(
            code,
            countedCode,
            ShouldBeRefusedWith(scenario.LastAnswer, HttpStatusCode.ServiceUnavailable, code));
        scenario.Metrics.Current(QueueLengthGauge).ShouldBe(queueLength);
    }

    #endregion

    #region Then — start-up entries

    [Then(@"^1 start-up entry names the email sender ""([^""]*)""$")]
    public void Then1StartupEntryNamesTheEmailSender(string sender)
    {
        var entry = StartupEntries(SenderStartedEvent).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Value("Sender").ShouldBe(sender);
    }

    [Then(@"^no warning says email is not set up$")]
    public void ThenNoWarningSaysEmailIsNotSetUp() =>
        StartupEntries(SenderNotConfiguredEvent).ShouldBeEmpty();

    [Then(@"^no email start-up entry is logged$")]
    public void ThenNoEmailStartupEntryIsLogged()
    {
        // The base settings keep email off on their own (no setting was given), and the capture saw the start-up.
        bool.Parse(scenario.Factory.Services.GetRequiredService<IConfiguration>()["Notifications:Email:Enabled"]
                .ShouldNotBeNull("the base settings hold no Notifications:Email:Enabled"))
            .ShouldBeFalse();
        scenario.StartupEntries.ShouldNotBeEmpty();
        StartupEntries(SenderStartedEvent).ShouldBeEmpty();
        StartupEntries(SenderNotConfiguredEvent).ShouldBeEmpty();
    }

    [Then(@"^the start-up wrote 1 warning that email is not set up, naming (.+) and no setting value$")]
    public void ThenTheStartupWrote1WarningThatEmailIsNotSetUp(string setting)
    {
        var expected = setting switch
        {
            "the mail server host" => "Notifications:Email:Smtp:Host",
            "the sender address" => "Notifications:Email:Smtp:FromAddress",
            "the port" => "Notifications:Email:Smtp:Port",
            "the security mode" => "Notifications:Email:Smtp:Security",
            "the time limit" => "Notifications:Email:TimeoutSeconds",
            "the sender choice" => "Notifications:Email:Sender",
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "no such setting in the spec")
        };

        var entry = StartupEntries(SenderNotConfiguredEvent).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Value("Settings").ShouldBe(expected);
        StartupEntries(SenderStartedEvent).ShouldBeEmpty();

        var texts = entry.State.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Append(entry.Message)
            .OfType<string>()
            .ToArray();
        _forbiddenValues.ShouldNotBeEmpty();
        foreach (var value in _forbiddenValues)
        {
            // A whole value, so a port such as "70000" is not found inside a longer number.
            var pattern = $"(?<![A-Za-z0-9]){Regex.Escape(value)}(?![A-Za-z0-9])";
            texts.ShouldAllBe(text => !Regex.IsMatch(text, pattern), $"the warning holds the value {value}");
        }
    }

    #endregion

    private async Task StartAsync(IReadOnlyDictionary<string, string?>? settings) =>
        await scenario.StartAsync(signIn: true, withRedis: true, settings: settings);

    /// <summary>The step names email "set up": the start-up must have said so, or the scenario tests nothing.</summary>
    private void ShouldHaveStartedTheEmailSender()
    {
        StartupEntries(SenderStartedEvent).ShouldHaveSingleItem().Value("Sender").ShouldBe("Smtp");
        StartupEntries(SenderNotConfiguredEvent).ShouldBeEmpty();
    }

    private IReadOnlyList<CapturedLogEntry> StartupEntries(string eventName) =>
        scenario.StartupEntries.Where(e => e.EventId.Name == eventName).ToArray();

    private void ShouldBeLoggedAndCounted(string code, string countedCode, string traceId)
    {
        countedCode.ShouldBe(code);
        var entry = scenario.SingleRejectionEntry(code, traceId, "treasury-ops");
        entry.Value("TemplateId").ShouldBe("account-opened");
        entry.Value("Channel").ShouldBe("email");
        scenario.Metrics.Sum(RejectedCounter, ("code", code)).ShouldBe(1);
        scenario.Metrics.Sum(RejectedCounter).ShouldBe(1);
    }

    /// <summary>Exactly one error, with <paramref name="code" /> and <paramref name="field" /> as the field at fault.</summary>
    private static void ShouldNameTheField(Answer answer, string code, string field)
    {
        using var json = JsonDocument.Parse(answer.Body);
        var error = json.RootElement.GetProperty("errors").EnumerateArray().ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe(code);
        error.GetProperty("field").GetString().ShouldBe(field);
    }

    /// <summary>A valid email call: the recipient and every token of the released template.</summary>
    private static string ValidEmailBody(string templateId, string to) =>
        Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = CustomerName,
            ["accountNumber"] = AccountNumber
        });

    /// <summary>The <c>to</c> value a recipient phrase stands for; null leaves the parameter out.</summary>
    private static string? Recipient(string phrase) => phrase switch
    {
        "no address" => null,
        "an empty address" => string.Empty,
        // 64 + 1 + 63 + 1 + 63 + 1 + 58 + 4: 255 characters, each label inside its own limit, so only the
        // length breaks the rule.
        "an address of 255 characters" => Recipient255(),
        _ when phrase.Length >= 2 && phrase[0] == '"' && phrase[^1] == '"' => phrase[1..^1],
        _ => throw new ArgumentOutOfRangeException(nameof(phrase), phrase, "no such recipient in the spec")
    };

    private static string Recipient255()
    {
        var address = $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 58)}.com";
        address.Length.ShouldBe(255);
        return address;
    }

    /// <summary>A fresh, valid key for a call whose step names none.</summary>
    private static string NewKey() => Guid.NewGuid().ToString("N");
}
