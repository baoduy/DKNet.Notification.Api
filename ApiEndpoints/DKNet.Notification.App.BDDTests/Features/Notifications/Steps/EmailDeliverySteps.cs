using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DKNet.Notification.Domains.Templates;
using Microsoft.Extensions.Logging;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// Steps for the delivery scenarios of <c>EmailChannel.feature</c> (DRK-2020 §5, surface B, brief DRK-2023 §7):
/// delivery, retries, repeated calls, the connection to the mail server, the health check and the local run. The
/// mail servers are real Mailpit containers (<see cref="MailCatcher" />); only the reply text that holds an address
/// comes from <see cref="ScriptedSmtpServer" />. Every expected value is a literal from the spec.
/// <c>NotificationStatus.feature</c> reuses these steps too.
/// </summary>
/// <remarks>
/// The entries are matched on the event names of the spec's log table, with the structured-state names this file
/// fixes: <c>NotificationAttemptFailed</c> (Warning: <c>Attempt</c>, <c>FailureKind</c> <c>transient</c> or
/// <c>permanent</c>, <c>ReplyCode</c>, empty with no reply), <c>NotificationDelivered</c> (Information:
/// <c>Attempt</c>, <c>Duration</c>) and <c>NotificationFailed</c> (Error: <c>AttemptCount</c>, <c>ReplyCode</c>),
/// each with <c>NotificationId</c>, <c>TemplateId</c>, <c>Channel</c>, <c>CallerId</c> and the accepting call's
/// <c>TraceId</c>. The instruments are <c>notifications.delivered</c> and <c>notifications.failed</c> (counters) and
/// <c>notifications.delivery.duration</c> (histogram), each tagged <c>channel</c>. The delivery worker's activity
/// comes from the source <c>DKNet.Notification</c> and links to the accepting call's trace.
/// </remarks>
[Binding]
[Scope(Feature = EmailChannelSteps.FeatureTitle)]
[Scope(Feature = NotificationStatusSteps.FeatureTitle)]
public sealed class EmailDeliverySteps(SendScenario scenario)
{
    public const string QueuedEvent = "NotificationQueued";
    public const string AttemptFailedEvent = "NotificationAttemptFailed";
    public const string DeliveredEvent = "NotificationDelivered";
    public const string FailedEvent = "NotificationFailed";
    public const string DeliveredCounter = "notifications.delivered";
    public const string FailedCounter = "notifications.failed";
    public const string DurationHistogram = "notifications.delivery.duration";
    public const string ActivitySourceName = "DKNet.Notification";

    // The spec's 2 sample values (§1 "Done means"), so a call that reaches rendering fills every released token.
    private const string CustomerName = "Jane Tan";
    private const string AccountNumber = "0012345678";

    // The sender of every mail: the settings MailCatcher.EmailSettings gives the service.
    private const string FromAddress = "notifications@drunkcoding.net";
    private const string FromName = "DKNet Notification";

    // Longer than the default wait before attempt 2 (5 s), so a second attempt would have started.
    private static readonly TimeSpan PastAttempt2 = TimeSpan.FromSeconds(7);

    private readonly ActivityTraceId _traceId = ActivityTraceId.CreateRandom();
    private readonly Dictionary<string, string> _notificationOf = new(StringComparer.Ordinal);
    private readonly HashSet<MailCatcher> _usedCatchers = [];

    private TraceCapture? _traces;
    private MailCatcher? _catcher;
    private MailCatcher? _chosenCatcher;
    private ScriptedSmtpServer? _scripted;
    private IReadOnlyDictionary<string, string?>? _settings;
    private Func<Task>? _clearFault;
    private string? _expectedReplyCode;
    private MailCatcher.Mail? _mail;
    private Answer? _probeAnswer;
    private Stopwatch? _sinceAccepted;

    private MailCatcher Catcher => _catcher.ShouldNotBeNull();

    [BeforeScenario]
    public async Task BeforeScenario()
    {
        _traces = new TraceCapture();
        scenario.TraceParent = $"00-{_traceId}-{ActivitySpanId.CreateRandom()}-01";
        await UseAsync(await MailCatcher.SharedAsync());
        await Catcher.ClearChaosAsync();
    }

    // After EmailChannelSteps has stopped the host, so nothing sends while the catchers are put back.
    [AfterScenario(Order = 20_000)]
    public async Task AfterScenario()
    {
        _traces?.Dispose();
        if (_scripted is not null)
        {
            await _scripted.DisposeAsync();
        }

        foreach (var catcher in _usedCatchers)
        {
            await catcher.EnsureRunningAsync();
            if (catcher.Kind == MailCatcherKind.StartTls)
            {
                await catcher.ClearChaosAsync();
            }
        }
    }

    #region Given — the service and its mail server

    [Given(@"^the mail catcher asks the service to sign in with the password ""([^""]*)""$")]
    public async Task GivenTheMailCatcherAsksTheServiceToSignInWithThePassword(string password)
    {
        var catcher = await MailCatcher.SharedAsync(MailCatcherKind.SignIn);
        MailCatcher.SignInPassword.ShouldBe(password);
        await StartAsync(catcher, SignedIn(catcher, MailCatcher.SignInUser, password));
    }

    [Given(@"^the template ""([^""]*)"" has the email subject ""([^""]*)""$")]
    public async Task GivenTheTemplateHasTheEmailSubject(string templateId, string subject)
    {
        // The subject lives in the template file's front matter, and the release ships no test file, so the test
        // template goes into the running catalogue: the released body with this subject. The call gives the
        // released body's account number too.
        await StartAsync(Catcher, Catcher.EmailSettings());
        var released = scenario.Factory.Gate.Find("account-opened").ShouldNotBeNull().Versions
            .Single(v => v.Channel == "email");
        scenario.Factory.Gate.Add(new NotificationTemplate(
            templateId, "A test template of DRK-2020.", [released with { Subject = subject }]));
    }

    [Given(@"^attempt 1 meets (.+)$")]
    public async Task GivenAttempt1Meets(string failure)
    {
        switch (failure)
        {
            case "the SMTP reply 451":
                await Catcher.FailEveryRecipientWithAsync(451);
                _expectedReplyCode = "451";
                _clearFault = Catcher.ClearChaosAsync;
                break;
            case "a mail server that does not answer in time":
                await Catcher.PauseAsync();
                _expectedReplyCode = string.Empty;
                _clearFault = Catcher.EnsureRunningAsync;
                break;
            case "a refused connection":
                await Catcher.StopAsync();
                _expectedReplyCode = string.Empty;
                _clearFault = Catcher.EnsureRunningAsync;
                break;
            case "the SMTP reply 550":
                await Catcher.FailEveryRecipientWithAsync(550);
                _expectedReplyCode = "550";
                break;
            case "the SMTP reply 535 for the sign-in":
                // The real reply of a mail server that requires sign-in, to a password it does not take.
                var catcher = await MailCatcher.SharedAsync(MailCatcherKind.SignIn);
                await StartAsync(catcher, SignedIn(catcher, MailCatcher.SignInUser, "not-the-password"));
                _expectedReplyCode = "535";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure), failure, "no such failure in the spec");
        }
    }

    [Given(@"^the mail catcher is stopped$")]
    public async Task GivenTheMailCatcherIsStopped() => await Catcher.StopAsync();

    [Given(@"^the mail server answers 451 to the first attempt for ""([^""]*)"" only$")]
    public async Task GivenTheMailServerAnswers451ToTheFirstAttemptFor(string to)
    {
        // Chaos answers every recipient: the When step turns it off once that first attempt has failed.
        await Catcher.FailEveryRecipientWithAsync(451);
        _expectedReplyCode = "451";
        _clearFault = Catcher.ClearChaosAsync;
    }

    [Given(@"^the mail server answers attempt 1 with ""([^""]*)""$")]
    public async Task GivenTheMailServerAnswersAttempt1With(string reply)
    {
        _scripted = new ScriptedSmtpServer(_ => reply);
        _expectedReplyCode = reply[..3];
        await StartAsync(catcher: null, _scripted.EmailSettings());
    }

    [Given(@"^the service runs with sign-in on and email set up to send to a mail server that (.+)$")]
    public async Task GivenTheServiceRunsWithEmailSetUpToSendToAMailServerThat(string server)
    {
        var catcher = server switch
        {
            "offers STARTTLS, with the security mode STARTTLS set" => await MailCatcher.SharedAsync(MailCatcherKind.StartTls),
            "speaks TLS from the first byte, with the security mode TLS set" => await MailCatcher.SharedAsync(MailCatcherKind.TlsOnConnect),
            _ => throw new ArgumentOutOfRangeException(nameof(server), server, "no such mail server in the spec")
        };
        var settings = catcher.EmailSettings();
        settings["Notifications:Email:Smtp:Security"].ShouldBe(catcher.Kind == MailCatcherKind.TlsOnConnect ? "Tls" : "StartTls");
        await StartAsync(catcher, settings);
    }

    [Given(@"^the service runs with sign-in on and email set up to send to a mail catcher that (.+)$")]
    public async Task GivenTheServiceRunsWithEmailSetUpToSendToAMailCatcherThat(string catcher) =>
        _chosenCatcher = catcher switch
        {
            "offers no sign-in" => await MailCatcher.SharedAsync(MailCatcherKind.StartTls),
            $@"accepts only the user ""{MailCatcher.SignInUser}"" with ""{MailCatcher.SignInPassword}""" =>
                await MailCatcher.SharedAsync(MailCatcherKind.SignIn),
            _ => throw new ArgumentOutOfRangeException(nameof(catcher), catcher, "no such mail catcher in the spec")
        };

    [Given(@"^the SMTP settings hold (.+)$")]
    public async Task GivenTheSmtpSettingsHold(string credentials)
    {
        var catcher = _chosenCatcher.ShouldNotBeNull("a Given step must choose the mail catcher first");
        if (credentials == "no user name")
        {
            await StartAsync(catcher, catcher.EmailSettings());
            return;
        }

        var match = Regex.Match(credentials, @"^the user ""([^""]*)"" and the password ""([^""]*)""$");
        match.Success.ShouldBeTrue($"no such credentials in the spec: {credentials}");
        await StartAsync(catcher, SignedIn(catcher, match.Groups[1].Value, match.Groups[2].Value));
    }

    [Given(@"^the service runs with sign-in on and email set up to send to a mail server whose certificate the service does not trust$")]
    public async Task GivenTheServiceRunsWithEmailSetUpToAnUntrustedMailServer()
    {
        // The scenario pins no waits, so they are cut to 1 s each: 3 attempts end in a few seconds.
        var catcher = await MailCatcher.SharedAsync(MailCatcherKind.Untrusted);
        await StartAsync(catcher, new Dictionary<string, string?>(catcher.EmailSettings(), StringComparer.Ordinal)
        {
            ["Notifications:Delivery:RetryDelaysSeconds:0"] = "1",
            ["Notifications:Delivery:RetryDelaysSeconds:1"] = "1"
        });
    }

    [Given(@"^the service runs with the local-run settings: sign-in off and email set up to send to the mail catcher over STARTTLS$")]
    public async Task GivenTheServiceRunsWithTheLocalRunSettings()
    {
        // Development is the local run's environment. Only the mail catcher's address goes in, as the AppHost
        // gives it: email on, the sender, STARTTLS and the sender address come from the local-run settings.
        Catcher.Kind.ShouldBe(MailCatcherKind.StartTls);
        _settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Notifications:Email:Smtp:Host"] = Catcher.Host,
            ["Notifications:Email:Smtp:Port"] = Catcher.SmtpHostPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        await scenario.StartAsync(signIn: false, withRedis: true, environment: "Development", settings: _settings);
        ShouldHaveStartedTheEmailSender();
    }

    #endregion

    #region Given — callers and earlier calls

    [Given(@"^""([^""]*)"" is also a caller allowed to send notifications$")]
    public void GivenIsAlsoACallerAllowedToSendNotifications(string caller) => scenario.AllowCaller(caller);

    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" to ""([^""]*)""$")]
    public async Task GivenEmailedTemplateTo(string caller, string templateId, string to) =>
        await EmailAsync(caller, templateId, to, NewKey());

    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" to ""([^""]*)"", and it waits for attempt 2$")]
    public async Task GivenEmailedTemplateToAndItWaitsForAttempt2(string caller, string templateId, string to)
    {
        await EmailAsync(caller, templateId, to, NewKey());
        await AttemptFailureAsync(_notificationOf[to], 1, TimeSpan.FromSeconds(15));
    }

    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task GivenEmailedTemplateToWithKey(string caller, string templateId, string to, string key) =>
        await EmailAsync(caller, templateId, to, key);

    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)"" on its first token$")]
    public async Task GivenEmailedTemplateToWithKeyOnItsFirstToken(string caller, string templateId, string to, string key)
    {
        scenario.Callers[caller] = scenario.Callers[caller] with { Authorization = "Bearer first-token" };
        await EmailAsync(caller, templateId, to, key);
    }

    #endregion

    #region When

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)"" for customer ""([^""]*)"", a line feed and ""([^""]*)""$")]
    public async Task WhenEmailsTemplateToForCustomerALineFeedAnd(
        string caller,
        string templateId,
        string to,
        string firstLine,
        string secondLine) =>
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = $"{firstLine}\n{secondLine}",
            ["accountNumber"] = AccountNumber
        }));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)"" with ""([^""]*)"" set to ""([^""]*)"", then ""([^""]*)"" set to ""([^""]*)"", and account ""([^""]*)""$")]
    public async Task WhenEmailsTemplateToWithTwoNamesThatDifferOnlyInCase(
        string caller,
        string templateId,
        string to,
        string firstName,
        string firstValue,
        string secondName,
        string secondValue,
        string accountNumber)
    {
        string.Equals(firstName, secondName, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        firstName.ShouldNotBe(secondName);
        // JsonObject keeps both names, in this order, as the caller's body does.
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            [firstName] = firstValue,
            [secondName] = secondValue,
            ["accountNumber"] = accountNumber
        }));
    }

    [When(@"^the mail catcher starts again before attempt 2$")]
    public async Task WhenTheMailCatcherStartsAgainBeforeAttempt2()
    {
        var notificationId = _notificationOf.Values.ShouldHaveSingleItem();
        await AttemptFailureAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        await Catcher.EnsureRunningAsync();
        EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem();
        EntriesFor(DeliveredEvent, notificationId).ShouldBeEmpty("the catcher must be back before attempt 2");
    }

    [When(@"^""([^""]*)"" emails ""([^""]*)"" and then ""([^""]*)""$")]
    public async Task WhenEmailsAndThen(string caller, string first, string second)
    {
        await EmailAsync(caller, "account-opened", first, NewKey());
        await AttemptFailureAsync(_notificationOf[first], 1, TimeSpan.FromSeconds(15));
        await _clearFault.ShouldNotBeNull()();
        await EmailAsync(caller, "account-opened", second, NewKey());
    }

    [When(@"^the service is restarted after the mail catcher is back$")]
    public async Task WhenTheServiceIsRestartedAfterTheMailCatcherIsBack()
    {
        var notificationId = _notificationOf.Values.ShouldHaveSingleItem();
        await Catcher.EnsureRunningAsync();
        EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem();
        await scenario.StartAsync(signIn: true, withRedis: true, settings: _settings ?? Catcher.EmailSettings(), keepStore: true);
        ShouldHaveStartedTheEmailSender();
    }

    [When(@"^""([^""]*)"" sends the same call again with key ""([^""]*)""$")]
    public async Task WhenSendsTheSameCallAgainWithKey(string caller, string key) =>
        await scenario.SendAsync(caller, key, SameCallAs(caller, key));

    [When(@"^""([^""]*)"" sends the same call with key ""([^""]*)"" on a new token$")]
    public async Task WhenSendsTheSameCallWithKeyOnANewToken(string caller, string key)
    {
        var body = SameCallAs(caller, key);
        scenario.Callers[caller] = scenario.Callers[caller] with { Authorization = "Bearer new-token" };
        await scenario.SendAsync(caller, key, body);
    }

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task WhenEmailsTemplateToWithKey(string caller, string templateId, string to, string key) =>
        await scenario.SendAsync(caller, key, EmailBody(templateId, to, CustomerName, AccountNumber));

    [When(@"^the cluster's liveness probe asks the health check without a token$")]
    public async Task WhenTheLivenessProbeAsksTheHealthCheckWithoutAToken() =>
        _probeAnswer = await scenario.GetAsync("/healthz");

    [When(@"^Minh, a developer, emails template ""([^""]*)"" to ""([^""]*)"" for customer ""([^""]*)"" and account ""([^""]*)""$")]
    public async Task WhenMinhEmailsTemplateToForCustomerAndAccount(string templateId, string to, string customerName, string accountNumber)
    {
        // Sign-in is off in the local run, so the call carries no token.
        scenario.Callers.ShouldNotContainKey("Minh");
        await scenario.SendAsync("Minh", NewKey(), EmailBody(templateId, to, customerName, accountNumber));
        _sinceAccepted = Stopwatch.StartNew();
    }

    #endregion

    #region Then — the mail

    [Then(@"^the mail catcher holds 1 mail to ""([^""]*)"" with the subject ""([^""]*)""$")]
    public async Task ThenTheMailCatcherHolds1MailToWithTheSubject(string to, string subject) =>
        (await SingleMailToAsync(to)).Subject.ShouldBe(subject);

    [Then(@"^the mail catcher holds (\d+) mails? to ""([^""]*)""$")]
    [Then(@"^the mail catcher holds exactly (\d+) mails? to ""([^""]*)""$")]
    public async Task ThenTheMailCatcherHoldsMailsTo(int count, string to) =>
        (await MailsToAsync(to, count, TimeSpan.FromSeconds(15))).Length.ShouldBe(count);

    [Then(@"^the mail server holds 1 mail to ""([^""]*)"", received over (STARTTLS|TLS)$")]
    public async Task ThenTheMailServerHolds1MailToReceivedOver(string to, string encryption)
    {
        // The catcher takes mail over this encryption only (MailCatcher remarks), so holding the mail proves it.
        Catcher.Kind.ShouldBe(encryption == "TLS" ? MailCatcherKind.TlsOnConnect : MailCatcherKind.StartTls);
        await SingleMailToAsync(to);
    }

    [Then(@"^the mail says ""([^""]*)""$")]
    public async Task ThenTheMailSays(string text)
    {
        var content = await Catcher.MailContentAsync((_mail ?? await SingleMailToAsync(LastRecipient())).ID);
        VisibleText(content.HTML).ShouldContain(text);
    }

    [Then(@"^the mail comes from the sender address and sender name of the settings$")]
    public void ThenTheMailComesFromTheSenderOfTheSettings()
    {
        var mail = _mail.ShouldNotBeNull("a Then step must find the mail first");
        mail.From.Address.ShouldBe(FromAddress);
        mail.From.Name.ShouldBe(FromName);
    }

    [Then(@"^the mail has no other recipient, no attachment and an HTML body only$")]
    public async Task ThenTheMailHasNoOtherRecipientNoAttachmentAndAnHtmlBodyOnly()
    {
        var mail = _mail.ShouldNotBeNull("a Then step must find the mail first");
        ShouldHaveOnlyTheRecipient(mail, LastRecipient());
        mail.Attachments.ShouldBe(0);

        var content = await Catcher.MailContentAsync(mail.ID);
        content.Attachments.ShouldBeEmpty();
        content.Inline.ShouldBeEmpty();
        // Mailpit may make a Text from the HTML, so the mail's own top-level type is what says "HTML only".
        var contentType = (await Catcher.HeadersAsync(mail.ID))["Content-Type"].ShouldHaveSingleItem();
        contentType.Split(';')[0].Trim().ShouldBe("text/html");
    }

    [Then(@"^the mail shows ""([^""]*)"" as text, not in bold$")]
    public async Task ThenTheMailShowsAsTextNotInBold(string value)
    {
        var content = await Catcher.MailContentAsync((await SingleMailToAsync(LastRecipient())).ID);
        // The released body (§2), filled with the call's values: the value reads as it was sent.
        VisibleText(content.HTML).ShouldContain($"Dear {value}, your account {AccountNumber} is open.");
        Regex.IsMatch(content.HTML, @"<\s*b[\s>]", RegexOptions.IgnoreCase).ShouldBeFalse(content.HTML);
    }

    [Then(@"^the mail's subject is ""([^""]*)""$")]
    public async Task ThenTheMailsSubjectIs(string subject) =>
        (await SingleMailToAsync(LastRecipient())).Subject.ShouldBe(subject);

    [Then(@"^the mail has only the recipient ""([^""]*)""$")]
    public async Task ThenTheMailHasOnlyTheRecipient(string to)
    {
        var mail = await SingleMailToAsync(to);
        ShouldHaveOnlyTheRecipient(mail, to);
        (await Catcher.MailsAsync()).ShouldHaveSingleItem();
        (await Catcher.HeadersAsync(mail.ID)).Keys.ShouldNotContain("Bcc");
    }

    [Then(@"^the mail server receives no mail$")]
    public async Task ThenTheMailServerReceivesNoMail()
    {
        await EntryAsync(FailedEvent, NotificationIdOf(scenario.LastAnswer), _ => true, TimeSpan.FromSeconds(30));
        (await Catcher.MailCountAsync()).ShouldBe(0);
    }

    [Then(@"^within (\d+) seconds Minh can read 1 mail to ""([^""]*)"" in the mail catcher, with the subject ""([^""]*)""$")]
    public async Task ThenWithinSecondsMinhCanRead1MailTo(int seconds, string to, string subject)
    {
        var sinceAccepted = _sinceAccepted.ShouldNotBeNull();
        var mails = await MailsToAsync(to, 1, TimeSpan.FromSeconds(seconds) - sinceAccepted.Elapsed);
        sinceAccepted.Elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(seconds));
        mails.ShouldHaveSingleItem().Subject.ShouldBe(subject);
    }

    #endregion

    #region Then — logs, counts and traces

    [Then(@"^1 queued entry and 1 delivered entry on attempt 1 are logged for the notification id$")]
    public async Task Then1QueuedEntryAnd1DeliveredEntryOnAttempt1AreLogged()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var delivered = await DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        delivered.Value("Duration").ShouldNotBeNullOrWhiteSpace();
        EntriesFor(DeliveredEvent, notificationId).ShouldHaveSingleItem();
        EntriesFor(AttemptFailedEvent, notificationId).ShouldBeEmpty();
        QueuedEntry(notificationId).Value("QueueLength").ShouldNotBeNullOrWhiteSpace();
    }

    [Then(@"^both entries carry the trace id of the call$")]
    public async Task ThenBothEntriesCarryTheTraceIdOfTheCall()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var queued = QueuedEntry(notificationId);
        queued.Value("TraceId").ShouldNotBeNull().ShouldMatch($"^00-{_traceId}-[0-9a-f]{{16}}-01$");
        var delivered = await DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        delivered.Value("TraceId").ShouldBe(queued.Value("TraceId"));

        // One trace covers acceptance and delivery: the delivery worker's activity links to the call's trace.
        var linked = await EventuallyAsync(
            () => Task.FromResult(DeliveryActivities().FirstOrDefault(a => a.Links.Any(l => l.Context.TraceId == _traceId))),
            TimeSpan.FromSeconds(5),
            $"no {ActivitySourceName} activity links to the call's trace");
        linked.TraceId.ShouldNotBe(default);
    }

    [Then(@"^the accepted count for ""([^""]*)"" with outcome ""([^""]*)"" and the delivered count for ""([^""]*)"" each rose by 1$")]
    public async Task ThenTheAcceptedAndDeliveredCountsEachRoseBy1(string channel, string outcome, string deliveredChannel)
    {
        await DeliveryAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        scenario.Metrics.Sum(AcceptedCounter, ("channel", channel), ("outcome", outcome)).ShouldBe(1);
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter, ("channel", deliveredChannel)).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter).ShouldBe(1);
    }

    [Then(@"^1 delivery duration is recorded for ""([^""]*)""$")]
    public async Task Then1DeliveryDurationIsRecordedFor(string channel)
    {
        await DeliveryAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        var durations = scenario.Metrics.Measurements.Where(m => m.Instrument == DurationHistogram).ToArray();
        var duration = durations.ShouldHaveSingleItem();
        duration.Tags["channel"].ShouldBe(channel);
        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Then(@"^no log entry, trace or kept idempotency record holds ""([^""]*)"", ""([^""]*)"", ""([^""]*)"" or ""([^""]*)""$")]
    public async Task ThenNoLogEntryTraceOrKeptIdempotencyRecordHolds(string first, string second, string third, string fourth)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));

        // Each place holds something, so finding none of the values there means something.
        var logged = scenario.AllLoggedText();
        logged.ShouldNotBeEmpty();
        await EventuallyAsync(
            () => Task.FromResult(DeliveryActivities().FirstOrDefault()),
            TimeSpan.FromSeconds(5),
            $"no {ActivitySourceName} activity was recorded");
        var traced = _traces.ShouldNotBeNull().AllText();
        var kept = await RedisServer.DumpAsync();
        kept.ShouldNotBeEmpty("the call's idempotency record is kept");
        // No metric either (DRK-2020 "Must stay true"): every tag of every measurement.
        var tagged = scenario.Metrics.Measurements.SelectMany(m => m.Tags.Values).OfType<string>().ToArray();
        tagged.ShouldNotBeEmpty();

        foreach (var value in new[] { first, second, third, fourth })
        {
            logged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a log entry holds {value}");
            traced.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a trace holds {value}");
            tagged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a metric holds {value}");
            kept.ShouldAllBe(
                record => !record.Key.Contains(value, StringComparison.Ordinal) && !record.Value.Contains(value, StringComparison.Ordinal),
                $"an idempotency record holds {value}");
        }
    }

    [Then(@"^attempt 1 is logged as a ""(transient|permanent)"" failure$")]
    public async Task ThenAttempt1IsLoggedAsAFailure(string kind)
    {
        // A mail server that does not answer uses up the 30 s time limit first.
        var failure = await AttemptFailureAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(50));
        failure.Value("FailureKind").ShouldBe(kind);
        failure.Value("ReplyCode").ShouldBe(_expectedReplyCode.ShouldNotBeNull());
    }

    [Then(@"^the mail is delivered on attempt 2$")]
    public async Task ThenTheMailIsDeliveredOnAttempt2()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await _clearFault.ShouldNotBeNull()();
        await DeliveryAsync(notificationId, 2, TimeSpan.FromSeconds(20));
        EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem().Value("Attempt").ShouldBe("1");
        EntriesFor(FailedEvent, notificationId).ShouldBeEmpty();
        (await MailsToAsync(LastRecipient(), 1, TimeSpan.FromSeconds(5))).ShouldHaveSingleItem();
    }

    [Then(@"^the notification fails with no attempt 2$")]
    public async Task ThenTheNotificationFailsWithNoAttempt2()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var failed = await EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(15));
        ShouldBeFailedEntry(failed, notificationId, 1, _expectedReplyCode.ShouldNotBeNull());

        await Task.Delay(PastAttempt2);
        EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem().Value("Attempt").ShouldBe("1");
        EntriesFor(DeliveredEvent, notificationId).ShouldBeEmpty();
        scenario.Metrics.Sum(FailedCounter, ("channel", "email")).ShouldBe(1);
        (await Catcher.MailCountAsync()).ShouldBe(0);
    }

    [Then(@"^1 transient attempt failure and 1 delivery on attempt 2 are logged$")]
    public async Task Then1TransientAttemptFailureAnd1DeliveryOnAttempt2AreLogged()
    {
        var notificationId = _notificationOf.Values.ShouldHaveSingleItem();
        await DeliveryAsync(notificationId, 2, TimeSpan.FromSeconds(15));
        EntriesFor(DeliveredEvent, notificationId).ShouldHaveSingleItem();
        var failure = EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem();
        failure.Value("Attempt").ShouldBe("1");
        failure.Value("FailureKind").ShouldBe("transient");
        failure.Value("ReplyCode").ShouldBe(string.Empty);
    }

    [Then(@"^(\d+) attempts are made, attempt 2 no sooner than (\d+) seconds after attempt 1, and attempt 3 no sooner than (\d+) seconds after attempt 2$")]
    public async Task ThenAttemptsAreMadeNoSoonerThan(int attempts, int firstWait, int secondWait)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(60));

        // Each failure is logged as its attempt ends, and attempt n + 1 starts after the wait: the gap between 2
        // failure entries is at least the wait. A refused connection ends an attempt at once.
        var failures = EntriesFor(AttemptFailedEvent, notificationId).OrderBy(e => e.LoggedAt).ToArray();
        failures.Select(e => e.Value("Attempt")).ShouldBe(Enumerable.Range(1, attempts).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        (failures[1].LoggedAt - failures[0].LoggedAt).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(firstWait));
        (failures[2].LoggedAt - failures[1].LoggedAt).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(secondWait));

        await Task.Delay(TimeSpan.FromSeconds(2));
        EntriesFor(AttemptFailedEvent, notificationId).Length.ShouldBe(attempts);
    }

    [Then(@"^(\d+) attempt failures and 1 failure error with attempt count (\d+) are logged$")]
    public async Task ThenAttemptFailuresAnd1FailureErrorAreLogged(int failures, int attemptCount) =>
        await ShouldHaveFailedAfterTransientFailuresAsync(failures, attemptCount, TimeSpan.FromSeconds(60));

    [Then(@"^the notification fails after (\d+) transient attempt failures$")]
    public async Task ThenTheNotificationFailsAfterTransientAttemptFailures(int failures) =>
        await ShouldHaveFailedAfterTransientFailuresAsync(failures, failures, TimeSpan.FromSeconds(30));

    [Then(@"^the failed count for ""([^""]*)"" rose by 1$")]
    public async Task ThenTheFailedCountRoseBy1(string channel)
    {
        await EntryAsync(FailedEvent, NotificationIdOf(scenario.LastAnswer), _ => true, TimeSpan.FromSeconds(60));
        scenario.Metrics.Sum(FailedCounter, ("channel", channel)).ShouldBe(1);
        scenario.Metrics.Sum(FailedCounter).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter).ShouldBe(0);
    }

    [Then(@"^the mail to ""([^""]*)"" is delivered before the mail to ""([^""]*)""$")]
    public async Task ThenTheMailToIsDeliveredBeforeTheMailTo(string first, string second)
    {
        var earlier = await EntryAsync(DeliveredEvent, _notificationOf[first], _ => true, TimeSpan.FromSeconds(15));
        var later = await EntryAsync(DeliveredEvent, _notificationOf[second], _ => true, TimeSpan.FromSeconds(15));
        earlier.LoggedAt.ShouldBeLessThan(later.LoggedAt);

        var mails = await Catcher.MailsAsync();
        mails.Select(m => m.To.ShouldHaveSingleItem().Address).ShouldBe([first, second]);
    }

    [Then(@"^the mail to ""([^""]*)"" is delivered on attempt (\d+)$")]
    public async Task ThenTheMailToIsDeliveredOnAttempt(string to, int attempt)
    {
        var notificationId = _notificationOf[to];
        await DeliveryAsync(notificationId, attempt, TimeSpan.FromSeconds(15));
        var failure = EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem();
        failure.Value("FailureKind").ShouldBe("transient");
        failure.Value("ReplyCode").ShouldBe(_expectedReplyCode.ShouldNotBeNull());
    }

    [Then(@"^the attempt failure entry holds the reply code (\d+)$")]
    public async Task ThenTheAttemptFailureEntryHoldsTheReplyCode(string code)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var failure = await AttemptFailureAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        failure.Value("ReplyCode").ShouldBe(code);
        failure.Value("FailureKind").ShouldBe("permanent");
        // The server did give its reply text to the service.
        _scripted.ShouldNotBeNull().Recipients.ShouldBe([LastRecipient()]);
    }

    [Then(@"^no log entry holds ""([^""]*)""$")]
    public async Task ThenNoLogEntryHolds(string value)
    {
        await EntryAsync(FailedEvent, NotificationIdOf(scenario.LastAnswer), _ => true, TimeSpan.FromSeconds(15));
        var logged = scenario.AllLoggedText();
        logged.ShouldNotBeEmpty();
        logged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a log entry holds {value}");
    }

    #endregion

    #region Then — repeated calls and the health check

    [Then(@"^the second answer carries the same notification id as the first$")]
    public void ThenTheSecondAnswerCarriesTheSameNotificationIdAsTheFirst()
    {
        scenario.Answers.Count.ShouldBe(2);
        scenario.Answers.ShouldAllBe(a => a.Status == HttpStatusCode.OK);
        NotificationIdOf(scenario.Answers[1]).ShouldBe(NotificationIdOf(scenario.Answers[0]));
    }

    [Then(@"^exactly 1 queued entry is logged, and the queued and delivered counts for ""([^""]*)"" each rose by 1 only$")]
    public async Task ThenExactly1QueuedEntryIsLoggedAndTheCountsRoseBy1Only(string channel)
    {
        var notificationId = NotificationIdOf(scenario.Answers[0]);
        await DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        scenario.Entries(QueuedEvent).ShouldHaveSingleItem().Value("NotificationId").ShouldBe(notificationId);
        scenario.Entries(DeliveredEvent).ShouldHaveSingleItem();
        scenario.Metrics.Sum(AcceptedCounter, ("channel", channel), ("outcome", "queued")).ShouldBe(1);
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter, ("channel", channel)).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter).ShouldBe(1);
    }

    [Then(@"^""([^""]*)"" gets a different notification id from ""([^""]*)""$")]
    public void ThenGetsADifferentNotificationIdFrom(string caller, string other)
    {
        var mine = NotificationIdOf(scenario.Answers.Single(a => a.Caller == caller));
        var theirs = NotificationIdOf(scenario.Answers.Single(a => a.Caller == other));
        mine.ShouldNotBe(theirs);
    }

    [Then(@"^the answer is (\d+) with the status ""([^""]*)""$")]
    public void ThenTheAnswerIsWithTheStatus(int status, string healthStatus)
    {
        var answer = _probeAnswer.ShouldNotBeNull();
        answer.Status.ShouldBe((HttpStatusCode)status, answer.Body);
        using var json = JsonDocument.Parse(answer.Body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["status"]);
        json.RootElement.GetProperty("status").GetString().ShouldBe(healthStatus);
        Catcher.IsRunning.ShouldBeFalse("the mail catcher must still be stopped");
    }

    #endregion

    #region Helpers

    private async Task UseAsync(MailCatcher catcher)
    {
        _catcher = catcher;
        if (_usedCatchers.Add(catcher))
        {
            await catcher.EnsureRunningAsync();
            await catcher.ClearAsync();
        }
    }

    /// <summary>Starts the service with email set up to <paramref name="catcher" />, or to the scripted server.</summary>
    private async Task StartAsync(MailCatcher? catcher, IReadOnlyDictionary<string, string?> settings)
    {
        if (catcher is not null)
        {
            await UseAsync(catcher);
        }

        _settings = settings;
        await scenario.StartAsync(signIn: true, withRedis: true, settings: settings);
        ShouldHaveStartedTheEmailSender();
    }

    private static IReadOnlyDictionary<string, string?> SignedIn(MailCatcher catcher, string userName, string password) =>
        new Dictionary<string, string?>(catcher.EmailSettings(), StringComparer.Ordinal)
        {
            ["Notifications:Email:Smtp:UserName"] = userName,
            ["Notifications:Email:Smtp:Password"] = password
        };

    /// <summary>The step names email "set up": the start-up must have said so, or the scenario tests nothing.</summary>
    private void ShouldHaveStartedTheEmailSender()
    {
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderStartedEvent)
            .ShouldHaveSingleItem().Value("Sender").ShouldBe("Smtp");
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderNotConfiguredEvent).ShouldBeEmpty();
    }

    private async Task EmailAsync(string caller, string templateId, string to, string key) =>
        _notificationOf[to] = scenario.ShouldBeAccepted(
            await scenario.SendAsync(caller, key, EmailBody(templateId, to, CustomerName, AccountNumber)));

    private static string EmailBody(string templateId, string to, string customerName, string accountNumber) =>
        Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = customerName,
            ["accountNumber"] = accountNumber
        });

    private string SameCallAs(string caller, string key)
    {
        var last = scenario.LastCall.ShouldNotBeNull();
        last.Caller.ShouldBe(caller);
        last.Key.ShouldBe(key);
        return last.Body.ShouldNotBeNull();
    }

    /// <summary>The <c>to</c> of the last call sent.</summary>
    private string LastRecipient()
    {
        var body = JsonNode.Parse(scenario.LastCall.ShouldNotBeNull().Body.ShouldNotBeNull()).ShouldNotBeNull();
        return body["parameters"]!["to"]!.GetValue<string>();
    }

    private IEnumerable<Activity> DeliveryActivities() =>
        _traces.ShouldNotBeNull().Activities.Where(a => a.Source.Name == ActivitySourceName);

    private CapturedLogEntry[] EntriesFor(string eventName, string notificationId) =>
        scenario.Entries(eventName).Where(e => e.Value("NotificationId") == notificationId).ToArray();

    private CapturedLogEntry QueuedEntry(string notificationId)
    {
        var queued = EntriesFor(QueuedEvent, notificationId).ShouldHaveSingleItem();
        queued.Level.ShouldBe(LogLevel.Information);
        return queued;
    }

    /// <summary>
    /// Waits for the first <paramref name="eventName" /> entry of the notification that <paramref name="match" />
    /// takes, and checks the fields every notification entry holds: those of its queued entry.
    /// </summary>
    private async Task<CapturedLogEntry> EntryAsync(
        string eventName,
        string notificationId,
        Func<CapturedLogEntry, bool> match,
        TimeSpan timeout)
    {
        var entry = await EventuallyAsync(
            () => Task.FromResult(EntriesFor(eventName, notificationId).FirstOrDefault(match)),
            timeout,
            $"no {eventName} entry for notification {notificationId}");

        entry.Level.ShouldBe(eventName switch
        {
            AttemptFailedEvent => LogLevel.Warning,
            DeliveredEvent => LogLevel.Information,
            FailedEvent => LogLevel.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(eventName), eventName, "not a delivery entry")
        });
        var queued = QueuedEntry(notificationId);
        foreach (var field in new[] { "TemplateId", "Channel", "CallerId", "TraceId" })
        {
            entry.Value(field).ShouldNotBeNullOrWhiteSpace(field);
            entry.Value(field).ShouldBe(queued.Value(field), field);
        }

        return entry;
    }

    private async Task<CapturedLogEntry> AttemptFailureAsync(string notificationId, int attempt, TimeSpan timeout) =>
        await EntryAsync(AttemptFailedEvent, notificationId, e => e.Value("Attempt") == Text(attempt), timeout);

    private async Task<CapturedLogEntry> DeliveryAsync(string notificationId, int attempt, TimeSpan timeout)
    {
        var delivered = await EntryAsync(DeliveredEvent, notificationId, _ => true, timeout);
        delivered.Value("Attempt").ShouldBe(Text(attempt));
        return delivered;
    }

    private static void ShouldBeFailedEntry(CapturedLogEntry failed, string notificationId, int attemptCount, string replyCode)
    {
        failed.Value("NotificationId").ShouldBe(notificationId);
        failed.Value("AttemptCount").ShouldBe(Text(attemptCount));
        failed.Value("ReplyCode").ShouldBe(replyCode);
    }

    private async Task ShouldHaveFailedAfterTransientFailuresAsync(int failures, int attemptCount, TimeSpan timeout)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var failed = await EntryAsync(FailedEvent, notificationId, _ => true, timeout);
        ShouldBeFailedEntry(failed, notificationId, attemptCount, string.Empty);
        EntriesFor(FailedEvent, notificationId).ShouldHaveSingleItem();

        var attempts = EntriesFor(AttemptFailedEvent, notificationId).OrderBy(e => e.LoggedAt).ToArray();
        attempts.Select(e => e.Value("Attempt")).ShouldBe(Enumerable.Range(1, failures).Select(Text));
        attempts.ShouldAllBe(e => e.Value("FailureKind") == "transient" && e.Value("ReplyCode") == string.Empty);
        EntriesFor(DeliveredEvent, notificationId).ShouldBeEmpty();
    }

    /// <summary>Waits until <paramref name="to" /> has at least <paramref name="count" /> mails, then a little longer for a stray one.</summary>
    private async Task<MailCatcher.Mail[]> MailsToAsync(string to, int count, TimeSpan timeout)
    {
        await EventuallyAsync(
            async () => (await MailsOfAsync(to)).Length >= count ? (object)true : null,
            timeout,
            $"the mail catcher got fewer than {count} mails to {to}");
        await Task.Delay(TimeSpan.FromSeconds(1));
        var mails = await MailsOfAsync(to);
        mails.Length.ShouldBe(count, $"mails to {to}");
        return mails;
    }

    private async Task<MailCatcher.Mail> SingleMailToAsync(string to)
    {
        _mail = (await MailsToAsync(to, 1, TimeSpan.FromSeconds(15))).ShouldHaveSingleItem();
        return _mail;
    }

    private async Task<MailCatcher.Mail[]> MailsOfAsync(string to) =>
        (await Catcher.MailsAsync()).Where(m => m.To.Any(r => r.Address == to)).ToArray();

    private static void ShouldHaveOnlyTheRecipient(MailCatcher.Mail mail, string to)
    {
        mail.To.Select(r => r.Address).ShouldBe([to]);
        (mail.Cc ?? []).ShouldBeEmpty();
        (mail.Bcc ?? []).ShouldBeEmpty();
    }

    /// <summary>The text a reader sees: the HTML without its tags, its entities read.</summary>
    private static string VisibleText(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", string.Empty)).Trim();

    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe, TimeSpan timeout, string failure)
        where T : class
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            if (await probe() is { } found)
            {
                return found;
            }

            if (waited.Elapsed > timeout)
            {
                throw new ShouldAssertException($"{failure} within {timeout.TotalSeconds:0.#} s");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private static string Text(int number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A fresh, valid key for a call whose step names none.</summary>
    private static string NewKey() => Guid.NewGuid().ToString("N");

    #endregion
}
