using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.GraphScenario;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// Steps for <c>GraphEmailSender.feature</c> (DRK-2028 §5, surface A, brief DRK-2033 §7): the Graph settings checked
/// at start-up, the sender change and the health check. Every expected value is a literal from the spec; the setting
/// names are the contract names of brief DRK-2033 §5 (<c>Notifications:Email:Graph:*</c>). Scoped to the feature, so
/// its step texts never clash with the email channel feature's.
/// </summary>
/// <remarks>
/// The start-up entries are the slice 3 ones (<see cref="EmailChannelSteps" /> remarks): <c>EmailSenderStarted</c>
/// (Information, <c>Sender</c>) and <c>EmailSenderNotConfigured</c> (Warning, <c>Settings</c>). The Graph stub and the
/// token stub are <see cref="RecordingHttpStub" />s of <see cref="GraphScenario" />, started fresh for each scenario,
/// and every host of the feature is pointed at them. The scenarios that send are in <see cref="GraphDeliverySteps" />.
/// </remarks>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class GraphEmailSenderSteps(SendScenario scenario, GraphScenario graph)
{
    public const string FeatureTitle = "Microsoft Graph email sender";

    private IReadOnlyDictionary<string, string?>? _startSettings;
    private string[] _forbiddenValues = [];
    private Answer? _probeAnswer;

    private MailCatcher MailCatcher => graph.MailCatcher;
    private RecordingHttpStub GraphStub => graph.GraphStub;
    private RecordingHttpStub TokenStub => graph.TokenStub;

    [BeforeScenario]
    public async Task BeforeScenario() => await graph.StartStubsAsync();

    [AfterScenario]
    public async Task AfterScenario()
    {
        await scenario.DisposeAsync();
        await graph.DisposeStubsAsync();
    }

    #region Given — the service

    [Given(@"^email is on, with the sender ""([^""]*)"", every Graph setting given and (.+)$")]
    public void GivenEmailIsOnWithTheSenderEveryGraphSettingGivenAnd(string sender, string credential)
    {
        var settings = GraphSettings();
        settings["Notifications:Email:Sender"] = sender;
        switch (credential)
        {
            case "the credential mode \"WorkloadIdentity\" and no client secret":
                settings[$"{Graph}:Credential"] = "WorkloadIdentity";
                break;
            case "the credential mode \"workloadidentity\" and no client secret":
                settings[$"{Graph}:Credential"] = "workloadidentity";
                break;
            case "the credential mode \"WorkloadIdentity\" and a client secret of 513 characters":
                settings[$"{Graph}:Credential"] = "WorkloadIdentity";
                settings[$"{Graph}:ClientSecret"] = Text(513);
                break;
            case "the credential mode \"ClientSecret\" and a client secret":
                settings[$"{Graph}:Credential"] = "ClientSecret";
                settings[$"{Graph}:ClientSecret"] = ClientSecret;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(credential), credential, "no such credential in the spec");
        }

        _startSettings = settings;
    }

    // Not the sender change's step, which also begins "the service started with email on and".
    [Given(@"^the service started with email on and (?!.*, with the mail catcher)(.+)$")]
    public async Task GivenTheServiceStartedWithEmailOnAnd(string fault)
    {
        // The Graph settings only, with no SMTP setting at all: with the sender Graph, the SMTP settings are not read
        // (brief R1), so the warning names the one Graph setting at fault and nothing else.
        var settings = GraphSettings();
        string? faultValue = null;
        switch (fault)
        {
            case "the sender \"Graph\" and no mailbox":
                settings.Remove($"{Graph}:Mailbox");
                break;
            case "the sender \"Graph\" and the mailbox \"notify.contoso.com\"":
                faultValue = settings[$"{Graph}:Mailbox"] = "notify.contoso.com";
                break;
            case "the sender \"Graph\" and a mailbox of 255 characters":
                faultValue = settings[$"{Graph}:Mailbox"] = Mailbox255();
                break;
            case "the sender \"Graph\" and the mailbox \"notify@contoso.com, ops@contoso.com\"":
                faultValue = settings[$"{Graph}:Mailbox"] = "notify@contoso.com, ops@contoso.com";
                break;
            case "the sender \"Graph\" and no tenant id":
                settings.Remove($"{Graph}:TenantId");
                break;
            case "the sender \"Graph\" and the tenant id \"contoso.onmicrosoft.com\"":
                faultValue = settings[$"{Graph}:TenantId"] = "contoso.onmicrosoft.com";
                break;
            case "the sender \"Graph\" and the client id \"mail-sender\"":
                faultValue = settings[$"{Graph}:ClientId"] = "mail-sender";
                break;
            case "the sender \"Graph\" and the credential mode \"Certificate\"":
                faultValue = settings[$"{Graph}:Credential"] = "Certificate";
                break;
            case "the sender \"Graph\", the credential mode \"ClientSecret\" and no client secret":
                settings[$"{Graph}:Credential"] = "ClientSecret";
                break;
            case "the sender \"Graph\", the credential mode \"ClientSecret\" and a client secret of 513 characters":
                settings[$"{Graph}:Credential"] = "ClientSecret";
                faultValue = settings[$"{Graph}:ClientSecret"] = Text(513);
                break;
            case "the sender \"SendGrid\"":
                faultValue = settings["Notifications:Email:Sender"] = "SendGrid";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "no such fault in the spec");
        }

        // Every value the warning must not hold. "Graph" and "true" are left out: they are part of setting names and
        // of ordinary text, not values only a leak would show.
        _forbiddenValues = new[]
            {
                faultValue,
                settings.GetValueOrDefault($"{Graph}:TenantId"),
                settings.GetValueOrDefault($"{Graph}:ClientId"),
                settings.GetValueOrDefault($"{Graph}:Mailbox"),
                settings.GetValueOrDefault($"{Graph}:ClientSecret")
            }
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await StartAsync(settings);
    }

    [Given(@"^the service started with email on and the sender ""([^""]*)"", with the mail catcher and the Graph stub ready$")]
    public async Task GivenTheServiceStartedWithTheSenderWithTheMailCatcherAndTheGraphStubReady(string sender)
    {
        // Every Graph setting is good too, so only the sender stands between the change and a Graph send.
        var settings = new Dictionary<string, string?>(MailCatcher.EmailSettings(), StringComparer.Ordinal);
        foreach (var (key, value) in GraphSettings().Where(s => s.Key.StartsWith(Graph, StringComparison.Ordinal)))
        {
            settings[key] = value;
        }

        settings["Notifications:Email:Sender"] = sender;
        await StartAsync(settings);
        StartupEntries(EmailChannelSteps.SenderStartedEvent).ShouldHaveSingleItem().Value("Sender").ShouldBe(sender);
        StartupEntries(EmailChannelSteps.SenderNotConfiguredEvent).ShouldBeEmpty();
    }

    [Given(@"^the sender is changed to ""([^""]*)"" while the service runs$")]
    public void GivenTheSenderIsChangedWhileTheServiceRuns(string sender)
    {
        scenario.Factory.Settings.Change("Notifications:Email:Sender", sender);

        // The change landed: the running service's settings now name the new sender.
        scenario.Factory.Services.GetRequiredService<IConfiguration>()["Notifications:Email:Sender"].ShouldBe(sender);
    }

    [Given(@"^the service runs with sign-in on and email set up to send through the Graph stub$")]
    public async Task GivenTheServiceRunsWithEmailSetUpToSendThroughTheGraphStub()
    {
        await StartAsync(GraphSettings());

        // The Graph settings landed in the running service, and the start-up named the Graph sender (brief DRK-2031
        // §7 slice notes).
        var configuration = scenario.Factory.Services.GetRequiredService<IConfiguration>();
        configuration["Notifications:Email:Sender"].ShouldBe("Graph");
        configuration[$"{Graph}:Mailbox"].ShouldBe(Mailbox);
        Then1StartupEntryNamesTheEmailSender("Graph");
    }

    [Given(@"^the Graph stub and the token stub are stopped$")]
    public async Task GivenTheGraphStubAndTheTokenStubAreStopped()
    {
        await GraphStub.StopAsync();
        await TokenStub.StopAsync();
    }

    [Given(@"^""([^""]*)"" is a caller allowed to send notifications$")]
    public void GivenIsACallerAllowedToSendNotifications(string caller) => scenario.AllowCaller(caller);

    #endregion

    #region When

    [When(@"^the service starts$")]
    public async Task WhenTheServiceStarts() =>
        await StartAsync(_startSettings.ShouldNotBeNull("a Given step must choose the email settings first"));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)""$")]
    public async Task WhenEmailsTemplateTo(string caller, string templateId, string to) =>
        await scenario.SendAsync(caller, Guid.NewGuid().ToString("N"), Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = "Jane Tan",
            ["accountNumber"] = "0012345678"
        }));

    [When(@"^the cluster's liveness probe asks the health check without a token$")]
    public async Task WhenTheLivenessProbeAsksTheHealthCheckWithoutAToken() =>
        _probeAnswer = await scenario.GetAsync("/healthz");

    #endregion

    #region Then

    [Then(@"^1 start-up entry names the email sender ""([^""]*)""$")]
    public void Then1StartupEntryNamesTheEmailSender(string sender)
    {
        var entry = StartupEntries(EmailChannelSteps.SenderStartedEvent).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Value("Sender").ShouldBe(sender);
    }

    [Then(@"^no warning says email is not set up$")]
    public void ThenNoWarningSaysEmailIsNotSetUp() =>
        StartupEntries(EmailChannelSteps.SenderNotConfiguredEvent).ShouldBeEmpty();

    [Then(@"^the call is accepted and skipped with reason ""([^""]*)"", and the Graph stub receives nothing$")]
    public async Task ThenTheCallIsAcceptedAndSkippedAndTheGraphStubReceivesNothing(string reason)
    {
        scenario.ShouldBeAccepted(scenario.LastAnswer);
        var entry = scenario.SingleSkipEntry();
        entry.Value("Reason").ShouldBe(reason);
        entry.Value("Channel").ShouldBe("email");
        scenario.Metrics.Sum(AcceptedCounter, ("channel", "email"), ("outcome", "skipped")).ShouldBe(1);
        await ThenTheGraphStubReceivesNothing();
    }

    [Then(@"^the start-up wrote 1 warning that email is not set up, naming (.+) and no setting value$")]
    public void ThenTheStartupWrote1WarningThatEmailIsNotSetUp(string setting)
    {
        var expected = setting switch
        {
            "the mailbox" => "Notifications:Email:Graph:Mailbox",
            "the tenant id" => "Notifications:Email:Graph:TenantId",
            "the client id" => "Notifications:Email:Graph:ClientId",
            "the credential mode" => "Notifications:Email:Graph:Credential",
            "the client secret" => "Notifications:Email:Graph:ClientSecret",
            "the sender choice" => "Notifications:Email:Sender",
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "no such setting in the spec")
        };

        var entry = StartupEntries(EmailChannelSteps.SenderNotConfiguredEvent).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Value("Settings").ShouldBe(expected);
        StartupEntries(EmailChannelSteps.SenderStartedEvent).ShouldBeEmpty();

        var texts = entry.State.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Append(entry.Message)
            .OfType<string>()
            .ToArray();
        _forbiddenValues.ShouldNotBeEmpty();
        foreach (var value in _forbiddenValues)
        {
            // A whole value, so a value is not found inside a longer one.
            var pattern = $"(?<![A-Za-z0-9]){Regex.Escape(value)}(?![A-Za-z0-9])";
            texts.ShouldAllBe(text => !Regex.IsMatch(text, pattern), $"the warning holds the value {value}");
        }
    }

    [Then(@"^the mail catcher receives 1 mail to ""([^""]*)""$")]
    public async Task ThenTheMailCatcherReceives1MailTo(string to)
    {
        var waited = Stopwatch.StartNew();
        while ((await MailsToAsync(to)).Length == 0)
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15), $"the mail catcher got no mail to {to}");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        // A short grace, so a second mail sent just after the first would still show.
        await Task.Delay(TimeSpan.FromSeconds(1));
        (await MailsToAsync(to)).ShouldHaveSingleItem();
    }

    [Then(@"^the Graph stub receives nothing$")]
    public async Task ThenTheGraphStubReceivesNothing()
    {
        GraphStub.IsRunning.ShouldBeTrue("the Graph stub must be ready to receive");
        // A short grace, so a send made just after the answer would still show.
        await Task.Delay(TimeSpan.FromSeconds(1));
        GraphStub.Requests.ShouldBeEmpty();
    }

    [Then(@"^the answer is (\d+) with the status ""([^""]*)""$")]
    public void ThenTheAnswerIsWithTheStatus(int status, string healthStatus)
    {
        var answer = _probeAnswer.ShouldNotBeNull();
        answer.Status.ShouldBe((HttpStatusCode)status, answer.Body);
        using var json = JsonDocument.Parse(answer.Body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["status"]);
        json.RootElement.GetProperty("status").GetString().ShouldBe(healthStatus);
        GraphStub.IsRunning.ShouldBeFalse("the Graph stub must still be stopped");
        TokenStub.IsRunning.ShouldBeFalse("the token stub must still be stopped");
    }

    #endregion

    private async Task StartAsync(IReadOnlyDictionary<string, string?> settings) => await graph.StartAsync(settings);

    private IReadOnlyList<CapturedLogEntry> StartupEntries(string eventName) =>
        scenario.StartupEntries.Where(e => e.EventId.Name == eventName).ToArray();

    private async Task<MailCatcher.Mail[]> MailsToAsync(string to) =>
        (await MailCatcher.MailsAsync()).Where(m => m.To.Any(r => r.Address == to)).ToArray();

    private static string Text(int length) => new('s', length);

    private static string Mailbox255()
    {
        // 64 + 1 + 63 + 1 + 63 + 1 + 58 + 4: 255 characters, each label inside its own limit, so only the length
        // breaks the rule.
        var address = $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 58)}.com";
        address.Length.ShouldBe(255);
        return address;
    }
}
