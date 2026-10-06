using System.Diagnostics;
using System.Text.Json.Nodes;
using DKNet.Notification.App.BDDTests.Features.Notifications.Steps;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.Client;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Client.Steps;

/// <summary>
/// Steps for the calls of <c>NotificationClient.feature</c> (DRK-2141 §5). Every expected value is a literal from
/// the spec, or the bytes the service sent where the spec says "the message the service sent".
/// </summary>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class NotificationClientSteps(ClientScenario client)
{
    public const string FeatureTitle = "Notification client package";

    // A recipient for a send whose step names none, and the account number the released template needs besides
    // the customer: the spec's sample values (DRK-2141 §5, DRK-2020 §1).
    private const string Jane = "jane@example.com";
    private const string AccountNumber = "0012345678";

    // The @unit stub's answers: the spec's id and key, and the service's own refusal body for a full queue.
    private const string StubNotificationId = "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47";
    private const string StubKey = "onboard-0012345678";

    // Every client step of the spec is a call of this caller.
    private const string CallerName = "accounts-api";

    private MailCatcher? _mailCatcher;
    private ScriptedService? _stub;

    private SendScenario Service => client.Service;

    private ScriptedService Stub
    {
        get => _stub.ShouldNotBeNull("a Given step must script the service first");
        set => _stub = value;
    }

    private MailCatcher MailCatcher => _mailCatcher.ShouldNotBeNull();

    [BeforeScenario("integration")]
    public async Task BeforeScenario()
    {
        _mailCatcher = await MailCatcher.SharedAsync();
        await _mailCatcher.EnsureRunningAsync();
        await _mailCatcher.ClearAsync();
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        await client.DisposeAsync();
        await Service.DisposeAsync();
        if (_mailCatcher is not null)
        {
            await _mailCatcher.EnsureRunningAsync();
        }
    }

    #region Given — the service and the caller

    [Given(@"^the notification service has the template ""([^""]*)"" registered for email$")]
    public async Task GivenTheNotificationServiceHasTheTemplateRegisteredForEmail(string templateId)
    {
        await StartServiceAsync();
        Service.Factory.Gate.Find(templateId).ShouldNotBeNull().Versions.Select(v => v.Channel).ShouldContain("email");
    }

    [Given(@"^""([^""]*)"" uses the notification client with its own token handler$")]
    [Given(@"^""([^""]*)"" uses the notification client$")]
    public async Task GivenUsesTheNotificationClient(string caller) => await UseClientAsync(caller, allowed: true);

    [Given(@"^""([^""]*)"" holds a token without the ""([^""]*)"" permission$")]
    public async Task GivenHoldsATokenWithoutThePermission(string caller, string permission)
    {
        permission.ShouldBe(Permission);
        await UseClientAsync(caller, allowed: false);
    }

    [Given(@"^""([^""]*)"" sent ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)"" and got notification id ""([^""]*)""$")]
    public async Task GivenSentToWithKeyAndGotNotificationId(
        string caller,
        string templateId,
        string to,
        string key,
        string notificationId) =>
        await SentAndBindAsync(caller, templateId, to, key, notificationId);

    // The recipient is the one the scenario's next step names: "the email to "jane@example.com" is delivered".
    [Given(@"^""([^""]*)"" sent ""([^""]*)"" with key ""([^""]*)"" and got notification id ""([^""]*)""$")]
    public async Task GivenSentWithKeyAndGotNotificationId(string caller, string templateId, string key, string notificationId) =>
        await SentAndBindAsync(caller, templateId, Jane, key, notificationId);

    [Given(@"^the email to ""([^""]*)"" is delivered$")]
    public async Task GivenTheEmailToIsDelivered(string to)
    {
        client.LastRequest.ShouldNotBeNull().Parameters["to"].ShouldBe(to);
        await WaitForMailAsync(to, TimeSpan.FromSeconds(10));
        // Delivered as the service records it, read with a plain call, before the client reads it.
        var id = client.LastSend.ShouldNotBeNull().NotificationId.ToString();
        var waited = Stopwatch.StartNew();
        while (StatusOf(await Service.ReadStatusAsync(CallerName, id)) != "success")
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the service must record the delivery");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    #endregion

    #region Given — the @unit service stub

    [Given(@"^the service answers every send from ""([^""]*)"" with status (\d+) and code ""([^""]*)""$")]
    public void GivenTheServiceAnswersEverySendWithStatusAndCode(string caller, int status, string code)
    {
        caller.ShouldBe(CallerName);
        Stub = new ScriptedService(request => request.Method == HttpMethod.Post
            ? Problem((HttpStatusCode)status, code)
            : Answer(request));
        client.Register(ClientScenario.StubAddress, () => Stub, withTokenHandler: true);
    }

    [Given(@"^""([^""]*)"" registers the notification client with the service address only$")]
    public void GivenRegistersTheNotificationClientWithTheServiceAddressOnly(string caller) =>
        RegisterOnStub(caller, withTokenHandler: false);

    [Given(@"^""([^""]*)"" registers the notification client with the service address and its own token handler$")]
    public void GivenRegistersTheNotificationClientWithItsOwnTokenHandler(string caller) =>
        RegisterOnStub(caller, withTokenHandler: true);

    [Given(@"^""([^""]*)"" registers the notification client with its own token handler, which adds token ""([^""]*)""$")]
    public void GivenRegistersTheNotificationClientWithATokenHandlerThatAddsToken(string caller, string token)
    {
        client.Token.Authorization = $"Bearer {token}";
        RegisterOnStub(caller, withTokenHandler: true);
    }

    #endregion

    #region When

    [When(@"^""([^""]*)"" sends ""([^""]*)"" by email to ""([^""]*)"" for customer ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task WhenSendsByEmailToForCustomerWithKey(
        string caller,
        string templateId,
        string to,
        string customerName,
        string key)
    {
        caller.ShouldBe(CallerName);
        await client.SendAsync(Email(templateId, to, customerName), key);
    }

    [When(@"^""([^""]*)"" sends the same notification again with key ""([^""]*)""$")]
    public async Task WhenSendsTheSameNotificationAgainWithKey(string caller, string key)
    {
        caller.ShouldBe(CallerName);
        await client.SendAsync(client.LastRequest.ShouldNotBeNull(), key);
    }

    [When(@"^""([^""]*)"" reads the status of ""([^""]*)""$")]
    public async Task WhenReadsTheStatusOf(string caller, string notificationId)
    {
        caller.ShouldBe(CallerName);
        await client.ReadStatusAsync(client.Id(notificationId));
    }

    [When(@"^""([^""]*)"" reads the status of ""([^""]*)"", which (.+)$")]
    public async Task WhenReadsTheStatusOfWhich(string caller, string notificationId, string which)
    {
        caller.ShouldBe(CallerName);
        switch (which)
        {
            case "the service never issued":
                client.Ids.ShouldNotContainKey(notificationId);
                break;
            case "\"billing-api\" sent":
                Service.AllowCaller("billing-api");
                var answer = await Service.SendAsync("billing-api", NewKey(), EmailBody("account-opened", Jane, "Jane"));
                client.Ids[notificationId] = Guid.Parse(Service.ShouldBeAccepted(answer));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(which), which, "no such notification in the spec");
        }

        await client.ReadStatusAsync(client.Id(notificationId));
    }

    [When(@"^""([^""]*)"" sends template ""([^""]*)"" by email$")]
    public async Task WhenSendsTemplateByEmail(string caller, string templateId)
    {
        caller.ShouldBe(CallerName);
        await client.SendAsync(Email(templateId, Jane, "Jane"), NewKey());
    }

    [When(@"^""([^""]*)"" sends ""([^""]*)"" while the delivery queue is full$")]
    public async Task WhenSendsWhileTheDeliveryQueueIsFull(string caller, string templateId)
    {
        // A queue of 1 that holds 1 notification it cannot deliver, as the email feature fills it.
        await StartServiceAsync(("Notifications:Delivery:QueueCapacity", "1"));
        await UseClientAsync(caller, allowed: true);
        await MailCatcher.StopAsync();
        Service.ShouldBeAccepted(await Service.SendAsync(caller, NewKey(), EmailBody(templateId, Jane, "Jane")));
        var waited = Stopwatch.StartNew();
        while (Service.Entries(EmailDeliverySteps.AttemptFailedEvent).Count == 0
               || await RedisServer.ListLengthAsync(DeliverNotification.QueueName) < 1)
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15), "the notification must wait in the queue");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        await client.SendAsync(Email(templateId, Jane, "Jane"), NewKey());
    }

    [When(@"^""([^""]*)"" sends ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task WhenSendsToWithKey(string caller, string templateId, string to, string key)
    {
        caller.ShouldBe(CallerName);
        await client.SendAsync(Email(templateId, to, "Jane"), key);
    }

    [When(@"^""([^""]*)"" sends ""([^""]*)"" to ""([^""]*)"" with an empty key$")]
    public async Task WhenSendsToWithAnEmptyKey(string caller, string templateId, string to)
    {
        caller.ShouldBe(CallerName);
        await client.SendAsync(Email(templateId, to, "Jane"), string.Empty);
    }

    [When(@"^""([^""]*)"" sends a notification and then reads its status$")]
    public async Task WhenSendsANotificationAndThenReadsItsStatus(string caller)
    {
        caller.ShouldBe(CallerName);
        await client.SendAsync(Email("account-opened", Jane, "Jane"), NewKey());
        client.ShouldHaveReturned();
        await client.ReadStatusAsync(client.LastSend.ShouldNotBeNull().NotificationId);
    }

    #endregion

    #region Then — answers

    [Then(@"^""([^""]*)"" gets back the notification id the service issued$")]
    public void ThenGetsBackTheNotificationIdTheServiceIssued(string caller)
    {
        caller.ShouldBe(CallerName);
        client.ShouldHaveReturned();
        var id = client.LastSend.ShouldNotBeNull().NotificationId;
        id.ShouldNotBe(Guid.Empty);
        var wire = client.Token.Answers.ShouldHaveSingleItem();
        wire.Status.ShouldBe(HttpStatusCode.OK);
        id.ToString().ShouldBe(NotificationIdOf(new Answer(caller, wire.Status, wire.Body)));
        Service.Metrics.Sum(AcceptedCounter, ("channel", "email"), ("outcome", "queued")).ShouldBe(1);
    }

    [Then(@"^""([^""]*)"" gets notification id ""([^""]*)"" again$")]
    public void ThenGetsNotificationIdAgain(string caller, string notificationId)
    {
        caller.ShouldBe(CallerName);
        client.ShouldHaveReturned();
        client.LastSend.ShouldNotBeNull().NotificationId.ShouldBe(client.Id(notificationId));
        client.Token.Answers.Count(a => a.Method == HttpMethod.Post).ShouldBe(2);
    }

    [Then(@"^the service sends only (\d+) emails? to ""([^""]*)""$")]
    public async Task ThenTheServiceSendsOnlyEmailsTo(int count, string to)
    {
        await WaitForMailAsync(to, TimeSpan.FromSeconds(10));
        // A short grace, so a second mail sent just after the first would still show.
        await Task.Delay(TimeSpan.FromSeconds(1));
        (await MailsToAsync(to)).ShouldBe(count);
    }

    [Then(@"^""([^""]*)"" sees status ""([^""]*)"" with key ""([^""]*)""$")]
    public void ThenSeesStatusWithKey(string caller, string status, string key)
    {
        caller.ShouldBe(CallerName);
        client.ShouldHaveReturned();
        var answer = client.LastStatus.ShouldNotBeNull();
        answer.NotificationId.ShouldBe(client.LastSend.ShouldNotBeNull().NotificationId);
        answer.Status.ShouldBe(status switch
        {
            "pending" => NotificationStatus.Pending,
            "success" => NotificationStatus.Success,
            "failed" => NotificationStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "no such status in the spec")
        });
        answer.IdempotencyKey.ShouldBe(key);
    }

    [Then(@"^no status is returned$")]
    public void ThenNoStatusIsReturned()
    {
        client.Raised.ShouldNotBeNull();
        client.LastStatus.ShouldBeNull();
    }

    #endregion

    #region Then — refusals

    [Then(@"^the client raises its refusal with status (\d+) and code ""([^""]*)""$")]
    public void ThenTheClientRaisesItsRefusalWithStatusAndCode(int status, string code)
    {
        var refusal = client.Refusal();
        refusal.StatusCode.ShouldBe((HttpStatusCode)status);
        refusal.Errors.Select(e => e.Code).ShouldBe([code]);
        // Every entry exactly as the service sent it (R2, brief §6a D5).
        refusal.Errors.Select(e => (e.Code, e.Field, e.Message)).ShouldBe(WireErrors(LastWireAnswer()));
    }

    [Then(@"^the refusal carries the message the service sent for it$")]
    public void ThenTheRefusalCarriesTheMessageTheServiceSentForIt()
    {
        var sent = WireErrors(LastWireAnswer()).ShouldHaveSingleItem().Message;
        sent.ShouldNotBeNullOrWhiteSpace();
        client.Refusal().Errors.ShouldHaveSingleItem().Message.ShouldBe(sent);
    }

    [Then(@"^the refusal names (.+) as the part at fault$")]
    public void ThenTheRefusalNamesAsThePartAtFault(string part) =>
        client.Refusal().Errors.ShouldHaveSingleItem().Field.ShouldBe(part switch
        {
            "the template id" => "templateId",
            // The service sends a full queue with an empty field (probe of DRK-2145, NotificationsV1Endpoint.cs:84).
            "no part" => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(part), part, "no such part in the spec")
        });

    [Then(@"^the client raises its refusal with status (\d+) and an empty error list$")]
    public void ThenTheClientRaisesItsRefusalWithStatusAndAnEmptyErrorList(int status)
    {
        var refusal = client.Refusal();
        refusal.StatusCode.ShouldBe((HttpStatusCode)status);
        refusal.Errors.ShouldBeEmpty();
        LastWireAnswer().Body.ShouldBeEmpty();
    }

    [Then(@"^the client raises its refusal with status (\d+)$")]
    public void ThenTheClientRaisesItsRefusalWithStatus(int status) =>
        client.Refusal().StatusCode.ShouldBe((HttpStatusCode)status);

    [Then(@"^no notification is queued$")]
    public void ThenNoNotificationIsQueued()
    {
        // The send reached the service, and the service accepted nothing.
        LastWireAnswer().Method.ShouldBe(HttpMethod.Post);
        Service.Metrics.Sum(AcceptedCounter).ShouldBe(0);
    }

    #endregion

    #region Then — the @unit stub

    [Then(@"^the service receives exactly (\d+) sends?$")]
    public void ThenTheServiceReceivesExactlySends(int count)
    {
        Stub.Received.Count(r => r.Method == HttpMethod.Post).ShouldBe(count);
        Stub.Received.Count.ShouldBe(count);
    }

    [Then(@"^the send reaches the service with no credential$")]
    public void ThenTheSendReachesTheServiceWithNoCredential()
    {
        var send = Stub.Received.ShouldHaveSingleItem();
        send.Method.ShouldBe(HttpMethod.Post);
        send.Authorization.ShouldBeNull();
        client.Token.Runs.ShouldBe(0);
    }

    [Then(@"^the token handler ran on both requests$")]
    public void ThenTheTokenHandlerRanOnBothRequests()
    {
        client.ShouldHaveReturned();
        client.Token.Runs.ShouldBe(2);
        Stub.Received.Select(r => (r.Method, r.Path, r.Authorization)).ShouldBe([
            (HttpMethod.Post, "/v1/notifications", SharedToken),
            (HttpMethod.Get, $"/v1/notifications/{StubNotificationId}", SharedToken)
        ]);
    }

    [Then(@"^no log entry written by the client holds ""([^""]*)""$")]
    public void ThenNoLogEntryWrittenByTheClientHolds(string token)
    {
        client.ShouldHaveReturned();
        // The token did go out, and the capture did see the client's entries.
        Stub.Received.ShouldHaveSingleItem().Authorization.ShouldBe($"Bearer {token}");
        client.Logs.Entries.ShouldNotBeEmpty();
        var texts = client.Logs.Entries
            .SelectMany(e => new[] { e.Message, e.Exception?.ToString() }.Concat(e.State.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))))
            .Concat(client.Logs.Scopes.SelectMany(s => s.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))))
            .OfType<string>()
            .ToArray();
        texts.ShouldAllBe(text => !text.Contains(token, StringComparison.Ordinal));
    }

    [Then(@"^once the token handler stops adding a token, the next send reaches the service with no credential$")]
    public async Task ThenOnceTheTokenHandlerStopsAddingATokenTheNextSendReachesTheServiceWithNoCredential()
    {
        client.Token.Adding = false;
        await client.SendAsync(Email("account-opened", Jane, "Jane"), NewKey());
        client.ShouldHaveReturned();
        client.Token.Runs.ShouldBe(2);
        Stub.Received.Count.ShouldBe(2);
        Stub.Received[1].Authorization.ShouldBeNull();
    }

    #endregion

    private async Task StartServiceAsync(params (string Key, string Value)[] settings)
    {
        var all = new Dictionary<string, string?>(MailCatcher.EmailSettings(), StringComparer.Ordinal);
        foreach (var (key, value) in settings)
        {
            all[key] = value;
        }

        await Service.StartAsync(signIn: true, withRedis: true, settings: all);
        client.ServiceStarted = true;
    }

    /// <summary>
    /// Registers the client against the running host (started now if no step started it), with the caller's token
    /// handler carrying a token that has the send permission, or one that lacks it.
    /// </summary>
    private async Task UseClientAsync(string caller, bool allowed)
    {
        caller.ShouldBe(CallerName);
        if (!client.ServiceStarted)
        {
            await StartServiceAsync();
        }

        // The same caller for the plain calls a step makes on its behalf (a read of the status, a queue filler).
        Service.AllowCaller(caller);
        client.Token.Claims = allowed ? Service.Callers[caller].Claims : $"client_id={caller}";
        client.Register(Service.Factory.Server.BaseAddress, () => Service.Factory.Server.CreateHandler(), withTokenHandler: true);
    }

    private async Task SentAndBindAsync(string caller, string templateId, string to, string key, string notificationId)
    {
        await UseClientAsync(caller, allowed: true);
        await client.SendAsync(Email(templateId, to, "Jane"), key);
        client.ShouldHaveReturned();
        client.Ids[notificationId] = client.LastSend.ShouldNotBeNull().NotificationId;
    }

    private void RegisterOnStub(string caller, bool withTokenHandler)
    {
        caller.ShouldBe(CallerName);
        Stub = new ScriptedService(Answer);
        client.Register(ClientScenario.StubAddress, () => Stub, withTokenHandler);
    }

    private WireAnswer LastWireAnswer() => client.Token.Answers.LastOrDefault().ShouldNotBeNull("no answer came back");

    private async Task WaitForMailAsync(string to, TimeSpan limit)
    {
        var waited = Stopwatch.StartNew();
        while (await MailsToAsync(to) == 0)
        {
            waited.Elapsed.ShouldBeLessThan(limit, $"a mail to {to} must arrive");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private async Task<int> MailsToAsync(string to) =>
        (await MailCatcher.MailsAsync()).Count(m => m.To.Any(r => string.Equals(r.Address, to, StringComparison.Ordinal)));

    /// <summary>An email send of <paramref name="templateId" /> with every token of the released template.</summary>
    private static SendNotificationRequest Email(string templateId, string to, string customerName) =>
        new("email", templateId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["to"] = to,
            ["customerName"] = customerName,
            ["accountNumber"] = AccountNumber
        });

    private static string EmailBody(string templateId, string to, string customerName) =>
        Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = customerName,
            ["accountNumber"] = AccountNumber
        });

    private static string? StatusOf(Answer answer)
    {
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);
        using var json = JsonDocument.Parse(answer.Body);
        return json.RootElement.GetProperty("status").GetString();
    }

    /// <summary>The <c>errors[]</c> entries of a problem body, exactly as sent.</summary>
    private static (string? Code, string? Field, string Message)[] WireErrors(WireAnswer answer)
    {
        using var json = JsonDocument.Parse(answer.Body);
        return json.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("code").GetString(), e.GetProperty("field").GetString(), e.GetProperty("message").GetString()!))
            .ToArray();
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static HttpResponseMessage Answer(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, $$"""{"notificationId":"{{StubNotificationId}}"}""", "application/json")
            : Json(HttpStatusCode.OK, $$"""{"notificationId":"{{StubNotificationId}}","idempotencyKey":"{{StubKey}}","status":"pending"}""", "application/json");

    /// <summary>A refusal shaped as the service sends one (NotificationsV1Endpoint.cs:84).</summary>
    private static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        Json(status, $$"""{"type":"{{status}}","title":"Error","status":{{(int)status}},"errors":[{"message":"The delivery queue is full. Try again later.","code":"{{code}}","field":""}],"code":"{{code}}"}""", "application/problem+json");

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

}
