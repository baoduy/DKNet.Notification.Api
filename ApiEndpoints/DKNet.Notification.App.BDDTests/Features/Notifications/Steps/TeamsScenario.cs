using System.Diagnostics;
using System.Text.Json.Nodes;
using DKNet.Notification.Domains.Templates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.EmailDeliverySteps;
using StubRequest = DKNet.Notification.App.BDDTests.Support.RecordingHttpStub.Request;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The Teams side of one scenario of <c>TeamsChannel.feature</c> (DRK-2035 §5), shared by its step classes: the webhook
/// stub, the "elsewhere" stub a redirect points to, the mail catcher, the settings the service starts with, the test
/// Teams templates and the waits on the delivery log. Every expected value is a literal from the spec.
/// </summary>
/// <remarks>
/// The settings are the contract names of brief DRK-2036 §5: <c>Notifications:Teams:Enabled</c>,
/// <c>Notifications:Teams:TimeoutSeconds</c> and <c>Notifications:Teams:Destinations:{name}:WebhookUrl</c>. The test
/// host trusts the stubs' authority through the Teams trust seam only (<see cref="SendApiFactory" />). The log entries,
/// counters and activity are the slice 3 ones (<see cref="EmailDeliverySteps" /> remarks) with the channel
/// <c>teams</c>, and the HTTP status code as <c>ReplyCode</c> (empty with no answer).
/// </remarks>
public sealed class TeamsScenario(SendScenario scenario)
{
    public const string FeatureTitle = "Microsoft Teams channel";
    public const string Teams = "Notifications:Teams";
    public const string Caller = "treasury-ops";

    // The spec's sample values (§1 "Done means"), so a call that reaches rendering fills every token of its template.
    public const string CustomerName = "Jane Tan";
    public const string AccountNumber = "0012345678";
    public const string DoneMeansTitle = "Account {{accountNumber}} opened";
    public const string DoneMeansBody = "**{{customerName}}** opened account {{accountNumber}}.";

    /// <summary>
    /// The Teams time limit of the hosts whose step pins none: short, so the time-limit row ends soon. The spec's own
    /// default (30 seconds) is the settings' business, not the scenarios'.
    /// </summary>
    public const int TimeoutSeconds = 4;

    /// <summary>
    /// The posted message of brief DRK-2036 §5, exactly, with no title block and an empty text: the bytes every filled
    /// body adds to. Copied from the brief, never made by the service's own card writer.
    /// </summary>
    public const string EmptyMessage =
        """{"type":"message","attachments":[{"contentType":"application/vnd.microsoft.card.adaptive","content":{"type":"AdaptiveCard","$schema":"http://adaptivecards.io/schemas/adaptive-card.json","version":"1.4","body":[{"type":"TextBlock","text":"","wrap":true}]}}]}""";

    // A Teams Workflows webhook path: it holds neither a destination name nor the signature, which sits in the query.
    private const string WebhookPath = "/workflows/5d2c81f4a7/triggers/manual/paths/invoke";
    private const string StubSignature = "stub-s1gn-0042";

    private readonly ActivityTraceId _traceId = ActivityTraceId.CreateRandom();
    private RecordingHttpStub? _webhookStub;
    private RecordingHttpStub? _elsewhere;
    private RecordingHttpStub? _untrusted;
    private MailCatcher? _mailCatcher;
    private TraceCapture? _traces;

    /// <summary>The stub every destination "pointing at the webhook stub" posts to.</summary>
    public RecordingHttpStub WebhookStub => _webhookStub.ShouldNotBeNull();

    /// <summary>Stands in for <c>https://elsewhere.example</c>, the address a redirect names.</summary>
    public RecordingHttpStub Elsewhere => _elsewhere.ShouldNotBeNull();

    public MailCatcher MailCatcher => _mailCatcher.ShouldNotBeNull();

    public TraceCapture Traces => _traces.ShouldNotBeNull();

    public ActivityTraceId TraceId => _traceId;

    /// <summary>The settings the running service started with; a restart starts from them.</summary>
    public Dictionary<string, string?> Settings { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>The answer to each call of a scenario that makes several, by the destination, address or customer it was for.</summary>
    public Dictionary<string, SendScenario.Answer> AnswerOf { get; } = new(StringComparer.Ordinal);

    /// <summary>The customer value of the last call that named one.</summary>
    public string LastCustomer { get; set; } = CustomerName;

    /// <summary>The health check's answer to the probe.</summary>
    public SendScenario.Answer? ProbeAnswer { get; set; }

    /// <summary>The <c>ReplyCode</c> the scripted failure must log; empty for a failure with no answer.</summary>
    public string ExpectedCode { get; set; } = string.Empty;

    /// <summary>Ends a fault that must not outlast attempt 1, such as a stub that is not listening.</summary>
    public Func<Task>? ClearFault { get; set; }

    /// <summary>Every post the webhook stub received, in order.</summary>
    public IReadOnlyList<StubRequest> Posts => WebhookStub.Requests.Where(r => r.Method == "POST").ToArray();

    #region Life cycle

    public async Task StartStubsAsync()
    {
        _traces = new TraceCapture();
        scenario.TraceParent = $"00-{_traceId}-{ActivitySpanId.CreateRandom()}-01";
        _mailCatcher = await MailCatcher.SharedAsync();
        await _mailCatcher.EnsureRunningAsync();
        await _mailCatcher.ClearAsync();
        _webhookStub = await RecordingHttpStub.StartAsync();
        _elsewhere = await RecordingHttpStub.StartAsync();
    }

    public async Task DisposeStubsAsync()
    {
        await scenario.DisposeAsync();
        foreach (var stub in new[] { _webhookStub, _elsewhere, _untrusted })
        {
            if (stub is not null)
            {
                await stub.DisposeAsync();
            }
        }

        _traces?.Dispose();
        if (_mailCatcher is not null)
        {
            await _mailCatcher.EnsureRunningAsync();
        }
    }

    /// <summary>Starts (or restarts) the service with sign-in on, Redis and <paramref name="settings" />.</summary>
    public async Task StartAsync(IReadOnlyDictionary<string, string?> settings)
    {
        Settings = new Dictionary<string, string?>(settings, StringComparer.Ordinal);
        await scenario.StartAsync(signIn: true, withRedis: true, settings: Settings);
    }

    /// <summary>
    /// Points the webhook stub at a fresh stub whose certificate the untrusted authority signs: no host trusts it.
    /// </summary>
    public async Task<RecordingHttpStub> StartUntrustedStubAsync()
    {
        _untrusted = await RecordingHttpStub.StartAsync(TestCertificateAuthority.Untrusted);
        var trusted = WebhookStub;
        _webhookStub = _untrusted;
        await trusted.DisposeAsync();
        return _untrusted;
    }

    #endregion

    #region Settings

    /// <summary>Teams on, the scenarios' time limit, and each destination pointing at the webhook stub.</summary>
    public Dictionary<string, string?> TeamsSettings(params string[] destinations)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{Teams}:Enabled"] = "true",
            [$"{Teams}:TimeoutSeconds"] = Text(TimeoutSeconds)
        };
        foreach (var destination in destinations)
        {
            settings[$"{Teams}:Destinations:{destination}:WebhookUrl"] = WebhookUrl();
        }

        return settings;
    }

    /// <summary><see cref="TeamsSettings" /> with email set up to send to the mail catcher.</summary>
    public Dictionary<string, string?> TeamsAndEmailSettings(params string[] destinations)
    {
        var settings = new Dictionary<string, string?>(MailCatcher.EmailSettings(), StringComparer.Ordinal);
        foreach (var (key, value) in TeamsSettings(destinations))
        {
            settings[key] = value;
        }

        return settings;
    }

    /// <summary>A webhook URL of the webhook stub: its signature in the query, as Teams Workflows gives it.</summary>
    public string WebhookUrl(string signature = StubSignature, string path = WebhookPath) =>
        $"{WebhookStub.Address.GetLeftPart(UriPartial.Authority)}{path}?api-version=2016-06-01&sv=1.0&sig={signature}";

    /// <summary>The path every post to a destination of <see cref="WebhookUrl" /> carries.</summary>
    public static string PathOfWebhook => WebhookPath;

    public IConfiguration Configuration() => scenario.Factory.Services.GetRequiredService<IConfiguration>();

    /// <summary>The step names Teams "on": the running service's settings say so (read as settings, never through the service).</summary>
    public void ShouldHaveTeamsOn(params string[] destinations)
    {
        var configuration = Configuration();
        configuration[$"{Teams}:Enabled"].ShouldBe("true");
        foreach (var destination in destinations)
        {
            configuration[$"{Teams}:Destinations:{destination}:WebhookUrl"].ShouldStartWith(WebhookStub.Address.GetLeftPart(UriPartial.Authority));
        }
    }

    /// <summary>The step names email "set up": the start-up named the SMTP sender, or the scenario tests nothing.</summary>
    public void ShouldHaveStartedTheEmailSender()
    {
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderStartedEvent)
            .ShouldHaveSingleItem().Value("Sender").ShouldBe("Smtp");
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderNotConfiguredEvent).ShouldBeEmpty();
    }

    #endregion

    #region Templates and calls

    /// <summary>Adds a Teams version under <paramref name="templateId" /> to the running service's catalogue.</summary>
    public void AddTeamsTemplate(string templateId, string? title, string body) =>
        scenario.Factory.Gate.Add(new NotificationTemplate(
            templateId,
            "A test template of DRK-2035; the release ships no Teams version.",
            [new TemplateVersion("teams", $"{templateId}.teams.md", TemplateFormat.Markdown, Subject: null, title, body)]));

    /// <summary>The template of §1 "Done means", unless the scenario already brought one under that id.</summary>
    public void EnsureDoneMeansTemplate(string templateId)
    {
        if (scenario.Factory.Gate.Templates.All(t => t.TemplateId != templateId))
        {
            AddTeamsTemplate(templateId, DoneMeansTitle, DoneMeansBody);
        }
    }

    /// <summary>A <c>teams</c> call body. A null <paramref name="destination" /> leaves <c>teamsDestination</c> out.</summary>
    public static string TeamsBody(string templateId, string? destination, JsonObject values, string channel = "teams")
    {
        var parameters = new JsonObject();
        if (destination is not null)
        {
            parameters["teamsDestination"] = destination;
        }

        foreach (var (name, value) in values)
        {
            parameters[name] = value?.DeepClone();
        }

        return SendScenario.Body(channel, templateId, parameters);
    }

    /// <summary>The §1 "Done means" values.</summary>
    public static JsonObject DoneMeansValues(string customerName = CustomerName, string accountNumber = AccountNumber) =>
        new() { ["customerName"] = customerName, ["accountNumber"] = accountNumber };

    /// <summary>Sends a call and keeps its answer.</summary>
    public async Task<SendScenario.Answer> SendAsync(string body) =>
        await scenario.SendAsync(Caller, NewKey(), body);

    /// <summary>Sends a call and keeps its answer under <paramref name="label" />.</summary>
    public async Task<SendScenario.Answer> SendAsync(string label, string body) =>
        AnswerOf[label] = await SendAsync(body);

    /// <summary>The notification id of the accepted call made for <paramref name="label" />.</summary>
    public string NotificationOf(string label) => scenario.ShouldBeAccepted(AnswerOf[label]);

    #endregion

    #region The card

    /// <summary>
    /// The text blocks of the one Adaptive Card of a post, checking the message around them: a <c>message</c> with
    /// exactly 1 attachment, an Adaptive Card, whose body holds text blocks only.
    /// </summary>
    public static JsonElement[] TextBlocksOf(StubRequest post)
    {
        using var json = JsonDocument.Parse(post.Body);
        var message = json.RootElement;
        message.GetProperty("type").GetString().ShouldBe("message");
        var attachment = message.GetProperty("attachments").EnumerateArray().ShouldHaveSingleItem();
        attachment.GetProperty("contentType").GetString().ShouldBe("application/vnd.microsoft.card.adaptive");
        var card = attachment.GetProperty("content");
        card.GetProperty("type").GetString().ShouldBe("AdaptiveCard");
        var blocks = card.GetProperty("body").EnumerateArray().Select(b => b.Clone()).ToArray();
        foreach (var block in blocks)
        {
            block.GetProperty("type").GetString().ShouldBe("TextBlock");
        }

        return blocks;
    }

    public static string? TextOf(JsonElement block) => block.GetProperty("text").GetString();

    /// <summary>Waits until the webhook stub holds at least <paramref name="count" /> posts, then a little longer for a stray one.</summary>
    public async Task<IReadOnlyList<StubRequest>> PostsAsync(int count, TimeSpan? timeout = null)
    {
        await EventuallyAsync(
            () => Task.FromResult<object?>(Posts.Count >= count ? true : null),
            timeout ?? TimeSpan.FromSeconds(20),
            $"the webhook stub got fewer than {count} posts");
        await Task.Delay(TimeSpan.FromSeconds(1));
        return Posts;
    }

    /// <summary>The webhook stub is listening and, after a grace past any delivery, holds no request.</summary>
    public async Task ShouldReceiveNothingAsync()
    {
        WebhookStub.IsRunning.ShouldBeTrue("the webhook stub must be ready to receive");
        // Past attempt 1 of a call queued by mistake.
        await Task.Delay(TimeSpan.FromSeconds(2));
        WebhookStub.Requests.ShouldBeEmpty();
    }

    #endregion

    #region The delivery log

    public CapturedLogEntry[] EntriesFor(string eventName, string notificationId) =>
        scenario.Entries(eventName).Where(e => e.Value("NotificationId") == notificationId).ToArray();

    public CapturedLogEntry QueuedEntry(string notificationId)
    {
        var queued = EntriesFor(QueuedEvent, notificationId).ShouldHaveSingleItem();
        queued.Level.ShouldBe(LogLevel.Information);
        return queued;
    }

    /// <summary>
    /// Waits for the first <paramref name="eventName" /> entry of the notification that <paramref name="match" />
    /// takes, and checks the fields every notification entry holds: those of its queued entry, channel <c>teams</c>
    /// for a Teams notification.
    /// </summary>
    public async Task<CapturedLogEntry> EntryAsync(
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

    public async Task<CapturedLogEntry> AttemptFailureAsync(string notificationId, int attempt, TimeSpan timeout) =>
        await EntryAsync(AttemptFailedEvent, notificationId, e => e.Value("Attempt") == Text(attempt), timeout);

    public async Task<CapturedLogEntry> DeliveryAsync(string notificationId, int attempt, TimeSpan timeout)
    {
        var delivered = await EntryAsync(DeliveredEvent, notificationId, _ => true, timeout);
        delivered.Value("Attempt").ShouldBe(Text(attempt));
        return delivered;
    }

    public IEnumerable<Activity> DeliveryActivities() =>
        Traces.Activities.Where(a => a.Source.Name == ActivitySourceName);

    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe, TimeSpan timeout, string failure)
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

    #endregion

    public static string Text(int number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A fresh, valid key for a call whose step names none.</summary>
    public static string NewKey() => Guid.NewGuid().ToString("N");
}
