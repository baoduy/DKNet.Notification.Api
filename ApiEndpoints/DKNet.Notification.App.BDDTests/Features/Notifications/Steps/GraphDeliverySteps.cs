using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;
using DKNet.Notification.AppServices.Delivery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.EmailDeliverySteps;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.GraphScenario;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;
using Reply = DKNet.Notification.App.BDDTests.Support.RecordingHttpStub.Reply;
using StubRequest = DKNet.Notification.App.BDDTests.Support.RecordingHttpStub.Request;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// Steps for the scenarios of <c>GraphEmailSender.feature</c> that send through the Graph stub and the token stub
/// (DRK-2028 §5, surface B, brief DRK-2031 §7): the send, the sender choice, the sign-in, workload identity, the
/// retry kinds and the wait after a 429. Every expected value is a literal from the spec.
/// </summary>
/// <remarks>
/// The log entries, counters and activity are the slice 3 ones (<see cref="EmailDeliverySteps" /> remarks), with the
/// HTTP status code of the failed step as <c>ReplyCode</c> (empty with no answer). A step scripts the stubs before the
/// call; a fault that must end before attempt 2 (a stub that is not listening) is ended once attempt 1 has failed.
/// </remarks>
[Binding]
[Scope(Feature = GraphEmailSenderSteps.FeatureTitle)]
public sealed class GraphDeliverySteps(SendScenario scenario, GraphScenario graph)
{
    // The spec's 2 sample values (§1 "Done means"), so a call that reaches rendering fills every released token.
    private const string CustomerName = "Jane Tan";
    private const string AccountNumber = "0012345678";

    // Longer than the wait before attempt 2 (5 s), so a second attempt would have started.
    private static readonly TimeSpan PastAttempt2 = TimeSpan.FromSeconds(7);

    // Past the attempt's time limit, so an answer this late comes after the attempt ended.
    private static readonly TimeSpan PastTheTimeLimit = TimeSpan.FromSeconds(GraphScenario.TimeoutSeconds + 4);

    // Each under the attempt's time limit, both together over it.
    private static readonly TimeSpan SlowStep = TimeSpan.FromSeconds(5);

    private readonly ActivityTraceId _traceId = ActivityTraceId.CreateRandom();
    private readonly Dictionary<string, string> _notificationOf = new(StringComparer.Ordinal);

    private TraceCapture? _traces;
    private Func<Task>? _clearFault;
    private string _expectedCode = string.Empty;
    private string? _errorStep;

    [BeforeScenario]
    public void BeforeScenario()
    {
        _traces = new TraceCapture();
        scenario.TraceParent = $"00-{_traceId}-{ActivitySpanId.CreateRandom()}-01";
    }

    [AfterScenario]
    public void AfterScenario() => _traces?.Dispose();

    #region Given — the service

    [Given(@"^the service runs with its released template catalogue, sign-in on and email set up to send through the Graph stub from the mailbox ""([^""]*)""$")]
    public async Task GivenTheServiceRunsWithEmailSetUpToSendThroughTheGraphStubFromTheMailbox(string mailbox)
    {
        mailbox.ShouldBe(Mailbox);
        await StartThroughGraphAsync(SendSettings());
    }

    [Given(@"^the service runs with its released template catalogue, sign-in on and email set up to send through the Graph stub, with the delivery waits of (\d+) seconds and (\d+) seconds$")]
    public async Task GivenTheServiceRunsWithEmailSetUpToSendThroughTheGraphStubWithTheDeliveryWaits(int first, int second)
    {
        var settings = SendSettings();
        settings["Notifications:Delivery:RetryDelaysSeconds:0"] = Text(first);
        settings["Notifications:Delivery:RetryDelaysSeconds:1"] = Text(second);
        await StartThroughGraphAsync(settings);
    }

    [Given(@"^the mail-sender app ""([^""]*)"" signs in with a client secret at the token stub$")]
    public void GivenTheMailSenderAppSignsInWithAClientSecretAtTheTokenStub(string app)
    {
        var configuration = Configuration();
        configuration[$"{Graph}:ClientId"].ShouldBe(app);
        configuration[$"{Graph}:Credential"].ShouldBe("ClientSecret");
        configuration[$"{Graph}:ClientSecret"].ShouldNotBeNullOrEmpty();
    }

    [Given(@"^the Graph sender signs in with the client secret ""([^""]*)"" at the token stub, which issues the token ""([^""]*)""$")]
    public void GivenTheGraphSenderSignsInWithTheClientSecretAtTheTokenStubWhichIssuesTheToken(string secret, string token)
    {
        var configuration = Configuration();
        configuration[$"{Graph}:Credential"].ShouldBe("ClientSecret");
        configuration[$"{Graph}:ClientSecret"].ShouldBe(secret);
        graph.IssuedToken = token;
    }

    [Given(@"^the service runs with sign-in on, email on with (.+), and both the mail catcher and the Graph stub ready$")]
    public async Task GivenTheServiceRunsWithEmailOnWithBothTheMailCatcherAndTheGraphStubReady(string senderChoice)
    {
        var settings = new Dictionary<string, string?>(graph.MailCatcher.EmailSettings(), StringComparer.Ordinal);
        foreach (var (key, value) in SendSettings().Where(s => s.Key.StartsWith(Graph, StringComparison.Ordinal)))
        {
            settings[key] = value;
        }

        var match = Regex.Match(senderChoice, @"^the sender ""([^""]*)""$");
        if (match.Success)
        {
            settings["Notifications:Email:Sender"] = match.Groups[1].Value;
        }
        else
        {
            senderChoice.ShouldBe("no sender given");
            settings.Remove("Notifications:Email:Sender");
        }

        await graph.StartAsync(settings);
        if (string.Equals(settings.GetValueOrDefault("Notifications:Email:Sender"), "Graph", StringComparison.OrdinalIgnoreCase))
        {
            graph.ShouldSendThroughGraph();
        }
        else
        {
            ShouldSendThroughSmtp();
        }
    }

    [Given(@"^the service started with email on, the sender ""([^""]*)"", every setting of that sender given, and (.+)$")]
    public async Task GivenTheServiceStartedWithEverysettingOfThatSenderGivenAnd(string sender, string otherFault)
    {
        var catcher = graph.MailCatcher.EmailSettings();
        Dictionary<string, string?> settings;
        switch ((sender, otherFault))
        {
            case ("Graph", "no mail server host"):
                // Every SMTP setting but the host, so only the host is at fault.
                settings = SendSettings();
                foreach (var (key, value) in catcher.Where(s => s.Key.StartsWith("Notifications:Email:Smtp:", StringComparison.Ordinal)))
                {
                    settings[key] = value;
                }

                settings.Remove("Notifications:Email:Smtp:Host").ShouldBeTrue();
                break;
            case ("Smtp", "the tenant id \"contoso.onmicrosoft.com\""):
                settings = new Dictionary<string, string?>(catcher, StringComparer.Ordinal);
                foreach (var (key, value) in SendSettings().Where(s => s.Key.StartsWith(Graph, StringComparison.Ordinal)))
                {
                    settings[key] = value;
                }

                settings[$"{Graph}:TenantId"] = "contoso.onmicrosoft.com";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(otherFault), otherFault, "no such sender and fault in the spec");
        }

        settings["Notifications:Email:Sender"].ShouldBe(sender);
        await graph.StartAsync(settings);
    }

    [Given(@"^the service runs with sign-in on and email set up to send through the Graph stub by workload identity, signing in at the token stub with the service account token ""([^""]*)""$")]
    public async Task GivenTheServiceRunsWithEmailSetUpByWorkloadIdentityWithTheServiceAccountToken(string token)
    {
        graph.GiveServiceAccountToken(token);
        await StartThroughGraphAsync(WorkloadIdentitySettings());
    }

    [Given(@"^the Graph settings also hold the client secret ""([^""]*)""$")]
    public async Task GivenTheGraphSettingsAlsoHoldTheClientSecret(string secret)
    {
        var settings = WorkloadIdentitySettings();
        settings[$"{Graph}:ClientSecret"] = secret;
        await StartThroughGraphAsync(settings);
        Configuration()[$"{Graph}:Credential"].ShouldBe("WorkloadIdentity");
    }

    [Given(@"^the service runs with sign-in on and email set up to send through the Graph stub by workload identity, with no service account token given to it$")]
    public async Task GivenTheServiceRunsWithEmailSetUpByWorkloadIdentityWithNoServiceAccountToken()
    {
        graph.ServiceAccountTokenFile.ShouldBeNull();
        await StartThroughGraphAsync(WorkloadIdentitySettings());
    }

    #endregion

    #region Given — what Graph and the token stub answer

    [Given(@"^Graph answers the send with HTTP (\d+)$")]
    public void GivenGraphAnswersTheSendWith(int status) => graph.GraphStub.DefaultReply = Reply.Status(status);

    [Given(@"^(Graph|the token stub) answers attempt 1 with (\d+) and the error text ""([^""]*)""$")]
    public void GivenAnswersAttempt1WithTheErrorText(string step, int status, string text)
    {
        _errorStep = step;
        _expectedCode = Text(status);
        if (step == "Graph")
        {
            graph.GraphStub.AnswerOnce(Reply.Status(status, GraphError("ErrorInvalidRecipients", text)));
        }
        else
        {
            graph.TokenStub.AnswerOnce(Reply.Status(status, TokenError("invalid_request", text)), IsTokenRequest);
        }
    }

    [Given(@"^the token stub answers the first token request with (.+)$")]
    public async Task GivenTheTokenStubAnswersTheFirstTokenRequestWith(string answer)
    {
        var stub = graph.TokenStub;
        switch (answer)
        {
            case "HTTP 503":
                Script(stub, 503, Reply.Status(503, TokenError("temporarily_unavailable", "The service is busy.")));
                break;
            case "HTTP 408":
                Script(stub, 408, Reply.Status(408, TokenError("request_timeout", "The request timed out.")));
                break;
            case "HTTP 429 with a retry after of 2 seconds":
                Script(stub, 429, Reply.Status(429, TokenError("too_many_requests", "Slow down."), ("Retry-After", "2")));
                break;
            case "no answer within the attempt's time limit":
                stub.AnswerOnce(Reply.Token(graph.IssuedToken) with { Delay = PastTheTimeLimit }, IsTokenRequest);
                break;
            case "a refused connection":
                await stub.PauseAsync();
                _clearFault = stub.ResumeAsync;
                break;
            case "a lost connection":
                stub.AnswerOnce(Reply.Lost(), IsTokenRequest);
                break;
            case "HTTP 401 with \"invalid_client\"":
                Script(stub, 401, Reply.Status(401, TokenError("invalid_client", "The client secret is not valid.")));
                break;
            case "HTTP 400 with \"invalid_request\"":
                Script(stub, 400, Reply.Status(400, TokenError("invalid_request", "The request is not valid.")));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "no such token answer in the spec");
        }
    }

    // Not the error-text step, which also begins "Graph answers attempt 1 with".
    [Given(@"^Graph answers attempt 1 with (?!\d+ and the error text )(.+)$")]
    public async Task GivenGraphAnswersAttempt1With(string answer)
    {
        var stub = graph.GraphStub;
        var status = Regex.Match(answer, @"^HTTP (\d+)$");
        var tooMany = Regex.Match(answer, "^429 and (.+)$");
        if (status.Success)
        {
            var code = int.Parse(status.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Script(stub, code, Reply.Status(code, code >= 500 || code == 408 ? null : GraphError("ErrorAccessDenied", "Access is denied.")));
        }
        else if (tooMany.Success)
        {
            Script(stub, 429, RetryAfterReply(tooMany.Groups[1].Value));
        }
        else
        {
            switch (answer)
            {
                case "a refused connection":
                    await stub.PauseAsync();
                    _clearFault = stub.ResumeAsync;
                    break;
                case "a lost connection":
                    stub.AnswerOnce(Reply.Lost());
                    break;
                case "a send it records but answers only after the attempt's time limit":
                    stub.AnswerOnce(Reply.Status(202) with { Delay = PastTheTimeLimit });
                    break;
                case "a slow token and a slow send that together pass the attempt's time limit":
                    // A token was not asked for yet, so attempt 1 makes the token request.
                    graph.TokenRequests.ShouldBeEmpty();
                    graph.TokenStub.AnswerOnce(Reply.Token(graph.IssuedToken) with { Delay = SlowStep }, IsTokenRequest);
                    stub.AnswerOnce(Reply.Status(202) with { Delay = SlowStep });
                    break;
                case "HTTP 302 to \"https://elsewhere.example\"":
                    // The elsewhere stub stands in for https://elsewhere.example, so a redirect followed would show.
                    Script(stub, 302, Reply.Status(302, null, ("Location", graph.Elsewhere.Address.ToString())));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(answer), answer, "no such Graph answer in the spec");
            }
        }
    }

    [Given(@"^Graph answers (\d+) to every send$")]
    public void GivenGraphAnswersToEverySend(int status)
    {
        _expectedCode = Text(status);
        graph.GraphStub.DefaultReply = Reply.Status(status);
    }

    [Given(@"^Graph answers the first send for ""([^""]*)"" with 429 and a retry after of (\d+) seconds$")]
    public void GivenGraphAnswersTheFirstSendForWith429AndARetryAfter(string to, int seconds)
    {
        _expectedCode = "429";
        graph.GraphStub.AnswerOnce(
            Reply.Status(429, GraphError("TooManyRequests", "Too many requests."), ("Retry-After", Text(seconds))),
            request => request.Body.Contains($"\"{to}\"", StringComparison.Ordinal));
    }

    #endregion

    #region When

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)"" for customer ""([^""]*)"" and account ""([^""]*)""$")]
    public async Task WhenEmailsTemplateToForCustomerAndAccount(string caller, string templateId, string to, string customerName, string accountNumber) =>
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = customerName,
            ["accountNumber"] = accountNumber
        }));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)"" with an extra parameter ""([^""]*)"" set to ""([^""]*)""$")]
    public async Task WhenEmailsTemplateToWithAnExtraParameter(string caller, string templateId, string to, string name, string value) =>
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = CustomerName,
            ["accountNumber"] = AccountNumber,
            [name] = value
        }));

    [When(@"^""([^""]*)"" emails ""([^""]*)"" and then ""([^""]*)""$")]
    public async Task WhenEmailsAndThen(string caller, string first, string second)
    {
        // In a row: the second call goes once the first one's attempt 1 has ended.
        await EmailAsync(caller, first);
        var firstId = _notificationOf[first];
        await EventuallyAsync(
            () => Task.FromResult<object?>(EntriesFor(AttemptFailedEvent, firstId).Concat(EntriesFor(DeliveredEvent, firstId)).FirstOrDefault()),
            TimeSpan.FromSeconds(30),
            $"attempt 1 of the mail to {first} did not end");
        await EmailAsync(caller, second);
    }

    #endregion

    #region Then — the send

    [Then(@"^the call is accepted with a new notification id$")]
    public void ThenTheCallIsAcceptedWithANewNotificationId() => scenario.ShouldBeAccepted(scenario.LastAnswer);

    [Then(@"^the Graph stub records 1 send from ""([^""]*)"" to ""([^""]*)"" only, with the subject ""([^""]*)""$")]
    public async Task ThenTheGraphStubRecords1SendFromToOnlyWithTheSubject(string from, string to, string subject)
    {
        await ThenTheGraphStubRecords1SendFromToOnly(from, to);
        MessageOf(graph.Sends.ShouldHaveSingleItem()).GetProperty("subject").GetString().ShouldBe(subject);
    }

    [Then(@"^the Graph stub records 1 send from ""([^""]*)"" to ""([^""]*)"" only$")]
    public async Task ThenTheGraphStubRecords1SendFromToOnly(string from, string to)
    {
        var send = (await SendsAsync(1)).ShouldHaveSingleItem();
        MailboxOf(send).ShouldBe(from);
        RecipientsOf(send).ShouldBe([to]);
    }

    [Then(@"^the send has an HTML body that says ""([^""]*)""$")]
    public async Task ThenTheSendHasAnHtmlBodyThatSays(string text)
    {
        var body = MessageOf((await SendsAsync(1)).ShouldHaveSingleItem()).GetProperty("body");
        body.GetProperty("contentType").GetString().ShouldBe("HTML");
        VisibleText(body.GetProperty("content").GetString().ShouldNotBeNull()).ShouldContain(text);
    }

    [Then(@"^the send has no copy recipient, no attachment, no sender address, no sender name and no Sent Items choice$")]
    public async Task ThenTheSendHasNoCopyRecipientNoAttachmentNoSenderAndNoSentItemsChoice()
    {
        // The design's sendMail body (03-integration) and nothing more: no ccRecipients, bccRecipients, attachments,
        // from, sender or saveToSentItems, and no display name with the one recipient.
        var send = (await SendsAsync(1)).ShouldHaveSingleItem();
        using var json = JsonDocument.Parse(send.Body);
        Names(json.RootElement).ShouldBe(["message"]);
        var message = json.RootElement.GetProperty("message");
        Names(message).ShouldBe(["body", "subject", "toRecipients"]);
        Names(message.GetProperty("body")).ShouldBe(["content", "contentType"]);
        var recipient = message.GetProperty("toRecipients").EnumerateArray().ShouldHaveSingleItem();
        Names(recipient).ShouldBe(["emailAddress"]);
        Names(recipient.GetProperty("emailAddress")).ShouldBe(["address"]);
    }

    [Then(@"^no send names ""([^""]*)""$")]
    public async Task ThenNoSendNames(string address)
    {
        var sends = await SendsAsync(1);
        sends.ShouldNotBeEmpty();
        graph.GraphStub.Requests.ShouldAllBe(
            r => !r.AllText().Contains(address, StringComparison.OrdinalIgnoreCase),
            $"a request to the Graph stub names {address}");
    }

    [Then(@"^the Graph stub records 1 send to ""([^""]*)""$")]
    public async Task ThenTheGraphStubRecords1SendTo(string to) =>
        RecipientsOf((await SendsAsync(1)).ShouldHaveSingleItem()).ShouldBe([to]);

    [Then(@"^the Graph stub receives 1 mail to ""([^""]*)""$")]
    public async Task ThenTheGraphStubReceives1MailTo(string to) => await ThenTheGraphStubRecords1SendTo(to);

    [Then(@"^the mail catcher receives nothing$")]
    public async Task ThenTheMailCatcherReceivesNothing()
    {
        graph.MailCatcher.IsRunning.ShouldBeTrue("the mail catcher must be ready to receive");
        // A grace past the other receiver's mail, so a mail sent as well would show.
        await Task.Delay(TimeSpan.FromSeconds(2));
        (await graph.MailCatcher.MailCountAsync()).ShouldBe(0);
    }

    [Then(@"^the Graph stub and the token stub receives nothing$")]
    public async Task ThenTheGraphStubAndTheTokenStubReceivesNothing()
    {
        graph.GraphStub.IsRunning.ShouldBeTrue("the Graph stub must be ready to receive");
        graph.TokenStub.IsRunning.ShouldBeTrue("the token stub must be ready to receive");
        await Task.Delay(TimeSpan.FromSeconds(2));
        graph.GraphStub.Requests.ShouldBeEmpty();
        graph.TokenStub.Requests.ShouldBeEmpty();
    }

    #endregion

    #region Then — the sign-in

    [Then(@"^the Graph stub records 2 sends, each with the token the token stub issued$")]
    public async Task ThenTheGraphStubRecords2SendsEachWithTheTokenTheTokenStubIssued()
    {
        var sends = await SendsAsync(2);
        sends.Count.ShouldBe(2);
        sends.Select(s => RecipientsOf(s).ShouldHaveSingleItem()).ShouldBe(_notificationOf.Keys.ToArray());
        sends.ShouldAllBe(s => s.Headers["Authorization"] == $"Bearer {graph.IssuedToken}");
    }

    [Then(@"^the token stub received 1 token request, from the app ""([^""]*)"", for Microsoft Graph$")]
    public void ThenTheTokenStubReceived1TokenRequestFromTheAppForMicrosoftGraph(string app)
    {
        var form = Form(graph.TokenRequests.ShouldHaveSingleItem());
        form["grant_type"].ShouldBe("client_credentials");
        form["client_id"].ShouldBe(app);
        form["scope"].ShouldBe("https://graph.microsoft.com/.default");
    }

    [Then(@"^the token stub received 1 token request that carries ""([^""]*)"" and not ""([^""]*)""$")]
    public async Task ThenTheTokenStubReceived1TokenRequestThatCarriesAndNot(string token, string secret)
    {
        await DeliveryAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        var request = graph.TokenRequests.ShouldHaveSingleItem();
        var form = Form(request);
        form["client_id"].ShouldBe(ClientId);
        form["client_assertion"].ShouldBe(token);
        request.AllText().ShouldNotContain(secret);
    }

    [Then(@"^the Graph stub records 1 send to ""([^""]*)"", and no log entry holds ""([^""]*)""$")]
    public async Task ThenTheGraphStubRecords1SendToAndNoLogEntryHolds(string to, string value)
    {
        await ThenTheGraphStubRecords1SendTo(to);
        await DeliveryAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        ShouldHaveNoLogEntryHolding(value);
    }

    #endregion

    #region Then — logs, counts and traces

    [Then(@"^1 delivered entry on attempt 1 is logged for the notification id$")]
    public async Task Then1DeliveredEntryOnAttempt1IsLogged()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        EntriesFor(DeliveredEvent, notificationId).ShouldHaveSingleItem();
        EntriesFor(AttemptFailedEvent, notificationId).ShouldBeEmpty();
    }

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
        var duration = scenario.Metrics.Measurements.Where(m => m.Instrument == DurationHistogram).ShouldHaveSingleItem();
        duration.Tags["channel"].ShouldBe(channel);
        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Then(@"^no log entry, trace or kept idempotency record holds ""([^""]*)"", ""([^""]*)"", ""([^""]*)"", ""([^""]*)"" or ""([^""]*)""$")]
    public async Task ThenNoLogEntryTraceOrKeptIdempotencyRecordHolds(string first, string second, string third, string fourth, string fifth)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        // The send did carry the token the scenario looks for.
        graph.Sends.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe($"Bearer {graph.IssuedToken}");

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
        var tagged = scenario.Metrics.Measurements.SelectMany(m => m.Tags.Values).OfType<string>().ToArray();
        tagged.ShouldNotBeEmpty();

        foreach (var value in new[] { first, second, third, fourth, fifth })
        {
            logged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a log entry holds {value}");
            traced.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a trace holds {value}");
            tagged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a metric holds {value}");
            kept.ShouldAllBe(
                record => !record.Key.Contains(value, StringComparison.Ordinal) && !record.Value.Contains(value, StringComparison.Ordinal),
                $"an idempotency record holds {value}");
        }
    }

    [Then(@"^the attempt failure entry holds the status code (\d+)$")]
    public async Task ThenTheAttemptFailureEntryHoldsTheStatusCode(string code)
    {
        var failure = await AttemptFailureAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        failure.Value("ReplyCode").ShouldBe(code);
        failure.Value("FailureKind").ShouldBe("permanent");
        // The step did give its error text to the service.
        (_errorStep == "Graph" ? graph.Sends : graph.TokenRequests).ShouldHaveSingleItem();
    }

    [Then(@"^no log entry holds ""([^""]*)""$")]
    public async Task ThenNoLogEntryHolds(string value)
    {
        await EntryAsync(FailedEvent, NotificationIdOf(scenario.LastAnswer), _ => true, TimeSpan.FromSeconds(15));
        ShouldHaveNoLogEntryHolding(value);
    }

    #endregion

    #region Then — attempts, retries and waits

    [Then(@"^attempt 1 is logged as a ""(transient|permanent)"" failure with (the status code \d+|no status code)$")]
    public async Task ThenAttempt1IsLoggedAsAFailureWith(string kind, string code)
    {
        var failure = await AttemptFailureAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(30));
        failure.Value("FailureKind").ShouldBe(kind);
        failure.Value("ReplyCode").ShouldBe(code == "no status code" ? string.Empty : code["the status code ".Length..]);
    }

    [Then(@"^the mail is sent on attempt 2$")]
    public async Task ThenTheMailIsSentOnAttempt2() => await ThenTheMailIsSentOnAttempt2And(null);

    [Then(@"^the mail is sent on attempt 2, (.+)$")]
    public async Task ThenTheMailIsSentOnAttempt2And(string? more)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var attempt1 = await AttemptFailureAsync(notificationId, 1, TimeSpan.FromSeconds(30));
        attempt1.Value("FailureKind").ShouldBe("transient");
        if (_clearFault is not null)
        {
            await _clearFault();
        }

        var delivered = await DeliveryAsync(notificationId, 2, TimeSpan.FromSeconds(45));
        EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem().Value("Attempt").ShouldBe("1");
        EntriesFor(FailedEvent, notificationId).ShouldBeEmpty();
        graph.Sends.ShouldNotBeEmpty();
        var sinceAttempt1 = delivered.LoggedAt - attempt1.LoggedAt;

        if (string.IsNullOrEmpty(more))
        {
            return;
        }

        Match match;
        if (more == "after 2 token requests in all")
        {
            graph.TokenRequests.Count.ShouldBe(2);
        }
        else if ((match = Regex.Match(more, @"^no sooner than (\d+) seconds and sooner than (\d+) seconds after attempt 1$")).Success)
        {
            sinceAttempt1.ShouldBeGreaterThanOrEqualTo(Seconds(match.Groups[1]));
            sinceAttempt1.ShouldBeLessThan(Seconds(match.Groups[2]));
        }
        else if ((match = Regex.Match(more, @"^no sooner than (\d+) seconds after attempt 1$")).Success)
        {
            sinceAttempt1.ShouldBeGreaterThanOrEqualTo(Seconds(match.Groups[1]));
        }
        else if ((match = Regex.Match(more, @"^sooner than (\d+) seconds after attempt 1$")).Success)
        {
            sinceAttempt1.ShouldBeLessThan(Seconds(match.Groups[1]));
        }
        else if ((match = Regex.Match(more, @"^and the Graph stub holds (\d+) sends to ""([^""]*)""$")).Success)
        {
            graph.Sends.Count(s => RecipientsOf(s).SequenceEqual([match.Groups[2].Value]))
                .ShouldBe(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(more), more, "no such outcome in the spec");
        }
    }

    [Then(@"^the notification fails with no attempt 2$")]
    public async Task ThenTheNotificationFailsWithNoAttempt2() => await ThenTheNotificationFailsWithNoAttempt2And(null);

    [Then(@"^the notification fails with no attempt 2, (.+)$")]
    public async Task ThenTheNotificationFailsWithNoAttempt2And(string? more)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var failed = await EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(30));
        failed.Value("AttemptCount").ShouldBe("1");
        failed.Value("ReplyCode").ShouldBe(_expectedCode);

        await Task.Delay(PastAttempt2);
        EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem().Value("Attempt").ShouldBe("1");
        EntriesFor(DeliveredEvent, notificationId).ShouldBeEmpty();
        scenario.Metrics.Sum(FailedCounter, ("channel", "email")).ShouldBe(1);

        switch (more)
        {
            case null or "":
                break;
            case "after 1 token request":
                graph.TokenRequests.Count.ShouldBe(1);
                break;
            case "and \"https://elsewhere.example\" receives nothing":
                graph.Sends.ShouldHaveSingleItem();
                graph.Elsewhere.IsRunning.ShouldBeTrue("the elsewhere stub must be ready to receive");
                graph.Elsewhere.Requests.ShouldBeEmpty();
                break;
            case "and the Graph stub receives nothing":
                graph.GraphStub.IsRunning.ShouldBeTrue("the Graph stub must be ready to receive");
                graph.GraphStub.Requests.ShouldBeEmpty();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(more), more, "no such outcome in the spec");
        }
    }

    [Then(@"^(\d+) attempts are made, with (\d+) sends in all$")]
    public async Task ThenAttemptsAreMadeWithSendsInAll(int attempts, int sends)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(60));
        await Task.Delay(TimeSpan.FromSeconds(2));
        EntriesFor(AttemptFailedEvent, notificationId).Length.ShouldBe(attempts);
        graph.Sends.Count.ShouldBe(sends);
    }

    [Then(@"^(\d+) attempt failures and 1 failure error with attempt count (\d+) are logged$")]
    public async Task ThenAttemptFailuresAnd1FailureErrorAreLogged(int failures, int attemptCount)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var failed = await EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(60));
        failed.Value("AttemptCount").ShouldBe(Text(attemptCount));
        failed.Value("ReplyCode").ShouldBe(_expectedCode);
        EntriesFor(FailedEvent, notificationId).ShouldHaveSingleItem();

        var attempts = EntriesFor(AttemptFailedEvent, notificationId).OrderBy(e => e.LoggedAt).ToArray();
        attempts.Select(e => e.Value("Attempt")).ShouldBe(Enumerable.Range(1, failures).Select(Text));
        attempts.ShouldAllBe(e => e.Value("FailureKind") == "transient" && e.Value("ReplyCode") == _expectedCode);
        EntriesFor(DeliveredEvent, notificationId).ShouldBeEmpty();
    }

    [Then(@"^the failed count for ""([^""]*)"" rose by 1$")]
    public async Task ThenTheFailedCountRoseBy1(string channel)
    {
        await EntryAsync(FailedEvent, NotificationIdOf(scenario.LastAnswer), _ => true, TimeSpan.FromSeconds(60));
        scenario.Metrics.Sum(FailedCounter, ("channel", channel)).ShouldBe(1);
        scenario.Metrics.Sum(FailedCounter).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter).ShouldBe(0);
    }

    [Then(@"^the mail to ""([^""]*)"" is sent before the mail to ""([^""]*)""$")]
    public async Task ThenTheMailToIsSentBeforeTheMailTo(string first, string second)
    {
        var earlier = await EntryAsync(DeliveredEvent, _notificationOf[first], _ => true, TimeSpan.FromSeconds(15));
        var later = await EntryAsync(DeliveredEvent, _notificationOf[second], _ => true, TimeSpan.FromSeconds(30));
        earlier.LoggedAt.ShouldBeLessThan(later.LoggedAt);

        // At the Graph stub: the second mail's first send, then the first mail's send again.
        graph.Sends.Select(s => RecipientsOf(s).ShouldHaveSingleItem()).ShouldBe([second, first, second]);
    }

    [Then(@"^the mail to ""([^""]*)"" is sent on attempt (\d+)$")]
    public async Task ThenTheMailToIsSentOnAttempt(string to, int attempt)
    {
        var notificationId = _notificationOf[to];
        await DeliveryAsync(notificationId, attempt, TimeSpan.FromSeconds(30));
        var failure = EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem();
        failure.Value("FailureKind").ShouldBe("transient");
        failure.Value("ReplyCode").ShouldBe(_expectedCode);
    }

    #endregion

    #region Helpers

    private async Task StartThroughGraphAsync(IReadOnlyDictionary<string, string?> settings)
    {
        await graph.StartAsync(settings);
        graph.ShouldSendThroughGraph();
    }

    private void ShouldSendThroughSmtp()
    {
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderStartedEvent)
            .ShouldHaveSingleItem().Value("Sender").ShouldBe("Smtp");
        scenario.Factory.Services.GetServices<IDeliverySender>().ShouldHaveSingleItem().ShouldBeOfType<SmtpEmailSender>();
    }

    /// <summary>
    /// <see cref="SendSettings" /> by workload identity with no client secret. No ambient Azure setting may give the
    /// credential a token file, an app, a tenant or a sign-in address: only the test host's seam does.
    /// </summary>
    private static Dictionary<string, string?> WorkloadIdentitySettings()
    {
        foreach (var name in new[] { "AZURE_FEDERATED_TOKEN_FILE", "AZURE_CLIENT_ID", "AZURE_TENANT_ID", "AZURE_AUTHORITY_HOST" })
        {
            Environment.GetEnvironmentVariable(name).ShouldBeNullOrEmpty($"the test run must not set {name}");
        }

        var settings = SendSettings();
        settings[$"{Graph}:Credential"] = "WorkloadIdentity";
        settings.Remove($"{Graph}:ClientSecret");
        return settings;
    }

    private IConfiguration Configuration() => scenario.Factory.Services.GetRequiredService<IConfiguration>();

    private void Script(RecordingHttpStub stub, int status, Reply reply)
    {
        _expectedCode = Text(status);
        if (ReferenceEquals(stub, graph.TokenStub))
        {
            stub.AnswerOnce(reply, IsTokenRequest);
        }
        else
        {
            stub.AnswerOnce(reply);
        }
    }

    private static bool IsTokenRequest(StubRequest request) =>
        request.Method == "POST" && request.Path == $"/{TenantId}/oauth2/v2.0/token";

    /// <summary>A 429 with the <c>Retry-After</c> header the spec's row names, made when the stub answers.</summary>
    private static Reply RetryAfterReply(string header)
    {
        var body = GraphError("TooManyRequests", "Too many requests.");
        var seconds = Regex.Match(header, @"^a retry after of (\d+) seconds$");
        if (seconds.Success)
        {
            return Reply.Status(429, body, ("Retry-After", seconds.Groups[1].Value));
        }

        return header switch
        {
            // An HTTP date has whole seconds: 3 seconds from now, up to the next whole second.
            "a retry-after date 3 seconds from now" => new Reply(429, body, () =>
                [("Retry-After", WholeSecondsUp(DateTimeOffset.UtcNow.AddSeconds(3)).ToString("R", System.Globalization.CultureInfo.InvariantCulture))]),
            "a retry-after date in the past" => new Reply(429, body, () =>
                [("Retry-After", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("R", System.Globalization.CultureInfo.InvariantCulture))]),
            "no retry-after header" => Reply.Status(429, body),
            "the retry-after value \"soon\"" => Reply.Status(429, body, ("Retry-After", "soon")),
            _ => throw new ArgumentOutOfRangeException(nameof(header), header, "no such retry-after header in the spec")
        };
    }

    private static DateTimeOffset WholeSecondsUp(DateTimeOffset time) =>
        DateTimeOffset.FromUnixTimeSeconds((time.ToUnixTimeMilliseconds() + 999) / 1000);

    /// <summary>A Microsoft Graph error body: its message is the provider's error text.</summary>
    private static string GraphError(string code, string message) =>
        new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    /// <summary>A Microsoft Entra ID token error body: its description is the provider's error text.</summary>
    private static string TokenError(string error, string description) =>
        new JsonObject { ["error"] = error, ["error_description"] = description }.ToJsonString();

    private static System.Collections.Specialized.NameValueCollection Form(StubRequest request) =>
        HttpUtility.ParseQueryString(request.Body);

    private static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Waits until the Graph stub holds at least <paramref name="count" /> sends, then a little longer for a stray one.</summary>
    private async Task<IReadOnlyList<StubRequest>> SendsAsync(int count)
    {
        await EventuallyAsync(
            () => Task.FromResult<object?>(graph.Sends.Count >= count ? (object)true : null),
            TimeSpan.FromSeconds(20),
            $"the Graph stub got fewer than {count} sends");
        await Task.Delay(TimeSpan.FromSeconds(1));
        return graph.Sends;
    }

    private void ShouldHaveNoLogEntryHolding(string value)
    {
        var logged = scenario.AllLoggedText();
        logged.ShouldNotBeEmpty();
        logged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a log entry holds {value}");
    }

    private async Task EmailAsync(string caller, string to) =>
        _notificationOf[to] = scenario.ShouldBeAccepted(await scenario.SendAsync(caller, NewKey(), Body("email", "account-opened", new JsonObject
        {
            ["to"] = to,
            ["customerName"] = CustomerName,
            ["accountNumber"] = AccountNumber
        })));

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

    /// <summary>The text a reader sees: the HTML without its tags, its entities read.</summary>
    private static string VisibleText(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", string.Empty)).Trim();

    private static TimeSpan Seconds(Group group) =>
        TimeSpan.FromSeconds(int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture));

    private static string Text(int number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A fresh, valid key for a call whose step names none.</summary>
    private static string NewKey() => Guid.NewGuid().ToString("N");

    #endregion
}
