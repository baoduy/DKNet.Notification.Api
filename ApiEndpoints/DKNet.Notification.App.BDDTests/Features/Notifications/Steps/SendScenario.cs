using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The state of one send scenario: its host, its callers and every answer, plus the request and check helpers the
/// step classes share. Every expected value here is a literal from DRK-2013.
/// </summary>
/// <remarks>
/// Log entries are matched on the event names and structured-state names this file fixes for the spec's
/// "Logs and metrics" fields: <c>NotificationSkipped</c> (Warning) and <c>NotificationRejected</c> (Information),
/// with <c>NotificationId</c>, <c>TemplateId</c>, <c>Channel</c>, <c>CallerId</c>, <c>TraceId</c>, <c>Reason</c>
/// and <c>Code</c>. The counters are <c>notifications.accepted</c> (tags <c>channel</c>, <c>outcome</c>) and
/// <c>notifications.rejected</c> (tag <c>code</c>) on the meter <c>DKNet.Notification</c>.
/// </remarks>
public sealed class SendScenario : IAsyncDisposable
{
    public const string Route = "/v1/notifications";
    public const string Permission = "notifications.send";
    public const string SkippedEvent = "NotificationSkipped";
    public const string RejectedEvent = "NotificationRejected";
    public const string AcceptedCounter = "notifications.accepted";
    public const string RejectedCounter = "notifications.rejected";

    /// <summary>The same bearer value for every allowed caller: only the caller claim tells 2 callers apart.</summary>
    public const string SharedToken = "Bearer shared-test-token";

    private SendApiFactory? _factory;
    private HttpClient? _client;
    private NotificationMetricsCapture? _metrics;

    public SendApiFactory Factory => _factory.ShouldNotBeNull("a Given step must start the service first");

    public NotificationMetricsCapture Metrics => _metrics.ShouldNotBeNull();

    public Dictionary<string, Credential> Callers { get; } = new(StringComparer.Ordinal);

    /// <summary>Every answer in the order it arrived. A held call joins only once it is released.</summary>
    public List<Answer> Answers { get; } = [];

    public SentCall? LastCall { get; private set; }

    /// <summary>When the first call of the scenario was sent.</summary>
    public Stopwatch? SinceFirstCall { get; private set; }

    public Task<Answer>? HeldCall { get; set; }

    /// <summary>A W3C <c>traceparent</c> every call of the scenario carries; null sends none.</summary>
    public string? TraceParent { get; set; }

    public Answer LastAnswer => Answers.Count > 0 ? Answers[^1] : throw new InvalidOperationException("no call was answered yet");

    public IReadOnlyList<CapturedLogEntry> SkipEntries => Entries(SkippedEvent);

    public IReadOnlyList<CapturedLogEntry> RejectionEntries => Entries(RejectedEvent);

    /// <summary>What the host logged while it started, kept before the capture is cleared for the scenario.</summary>
    public IReadOnlyList<CapturedLogEntry> StartupEntries { get; private set; } = [];

    #region Host

    /// <summary>
    /// Boots a fresh host and clears what its start-up logged, so the scenario sees only its own entries. A host
    /// the scenario started before is stopped first, as a restart stops it.
    /// </summary>
    /// <param name="keepStore">Keeps the Redis store as it is, as a restart of the service does; otherwise it is emptied.</param>
    public async Task StartAsync(
        bool signIn,
        bool withRedis,
        string environment = "Testing",
        IReadOnlyDictionary<string, string?>? settings = null,
        bool keepStore = false)
    {
        await StopHostAsync();
        string? redis = null;
        if (withRedis)
        {
            if (!keepStore)
            {
                await RedisServer.FlushAsync();
            }

            redis = await RedisServer.ConnectionStringAsync();
        }

        _factory = new SendApiFactory(signIn, redis, environment, settings);
        _client = _factory.CreateClient();
        _metrics = new NotificationMetricsCapture(_factory.Services.GetRequiredService<IMeterFactory>());
        StartupEntries = _factory.LogCapture.Entries.ToArray();
        _factory.LogCapture.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            _factory.Gate.Release();
        }

        if (HeldCall is not null)
        {
            await HeldCall;
        }

        await StopHostAsync();
    }

    private async Task StopHostAsync()
    {
        _metrics?.Dispose();
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _metrics = null;
        _client = null;
        _factory = null;
    }

    #endregion

    #region Callers

    /// <summary>A machine caller with the permission in its <c>roles</c> claim.</summary>
    public void AllowCaller(string caller) =>
        Callers[caller] = new Credential(SharedToken, $"client_id={caller};roles={Permission}");

    #endregion

    #region Requests

    public static string EmailBody(string templateId, string to) =>
        Body("email", templateId, new JsonObject { ["to"] = to });

    /// <summary>A request body. A null <paramref name="parameters" /> leaves the field out.</summary>
    public static string Body(string channel, string templateId, JsonObject? parameters)
    {
        var body = new JsonObject { ["channel"] = channel, ["templateId"] = templateId };
        if (parameters is not null)
        {
            body["parameters"] = parameters;
        }

        return body.ToJsonString();
    }

    /// <summary>A valid email body padded with trailing white space to exactly <paramref name="bytes" /> bytes.</summary>
    public static string BodyOfSize(int bytes)
    {
        var body = EmailBody("account-opened", "jane@example.com");
        var padded = body + new string(' ', bytes - Encoding.UTF8.GetByteCount(body));
        Encoding.UTF8.GetByteCount(padded).ShouldBe(bytes);
        return padded;
    }

    public static HttpContent Json(string body) => new StringContent(body, Encoding.UTF8, "application/json");

    /// <summary>A JSON body sent with no <c>Content-Length</c>, so its size is known only once it is read.</summary>
    public static HttpContent JsonOfUnknownLength(string body)
    {
        var content = new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    /// <summary>Sends one call and keeps its answer. A body that is not a <see cref="string" /> is never repeated.</summary>
    public async Task<Answer> SendAsync(string caller, string? key, string body) =>
        await SendAsync(caller, key, Json(body), body);

    public async Task<Answer> SendAsync(string caller, string? key, HttpContent content, string? repeatableBody = null)
    {
        var answer = await SendUnrecordedAsync(caller, key, content, repeatableBody);
        Answers.Add(answer);
        return answer;
    }

    /// <summary>Sends one call without keeping its answer, for a call held inside the service.</summary>
    public async Task<Answer> SendUnrecordedAsync(
        string caller,
        string? key,
        HttpContent content,
        string? repeatableBody = null)
    {
        _client.ShouldNotBeNull("a Given step must start the service first");
        SinceFirstCall ??= Stopwatch.StartNew();
        LastCall = new SentCall(caller, key, repeatableBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = content };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key).ShouldBeTrue();
        }

        if (TraceParent is not null)
        {
            request.Headers.TryAddWithoutValidation("traceparent", TraceParent).ShouldBeTrue();
        }

        if (Callers.TryGetValue(caller, out var credential))
        {
            if (credential.Authorization is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", credential.Authorization).ShouldBeTrue();
            }

            request.Headers.TryAddWithoutValidation(TestAuthHandler.ClaimsHeader, credential.Claims).ShouldBeTrue();
        }

        using var response = await _client.SendAsync(request);
        return new Answer(caller, response.StatusCode, await response.Content.ReadAsStringAsync())
        {
            RetryAfter = response.Headers.RetryAfter?.ToString()
        };
    }

    /// <summary>A <c>GET</c> with no token, as a probe sends it.</summary>
    public async Task<Answer> GetAsync(string path)
    {
        _client.ShouldNotBeNull("a Given step must start the service first");
        using var response = await _client.GetAsync(path);
        return new Answer("probe", response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    #endregion

    #region Checks

    public IReadOnlyList<CapturedLogEntry> Entries(string eventName) =>
        Factory.LogCapture.Entries.Where(e => e.EventId.Name == eventName).ToArray();

    /// <summary>202 with a body that holds only a notification id, new in this scenario.</summary>
    public string ShouldBeAccepted(Answer answer)
    {
        answer.Status.ShouldBe(HttpStatusCode.Accepted, answer.Body);
        var id = NotificationIdOf(answer);
        Answers.Where(a => !ReferenceEquals(a, answer) && a.Status == HttpStatusCode.Accepted)
            .Select(NotificationIdOf)
            .ShouldNotContain(id);
        return id;
    }

    /// <summary>The notification id of a 202, checking the body holds that field only.</summary>
    public static string NotificationIdOf(Answer answer)
    {
        using var json = JsonDocument.Parse(answer.Body);
        json.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["notificationId"]);
        var id = Guid.Parse(json.RootElement.GetProperty("notificationId").GetString()!);
        id.ShouldNotBe(Guid.Empty);
        return id.ToString();
    }

    /// <summary>400 problem details with a trace id and every error carrying <paramref name="code" />.</summary>
    public static string ShouldBeRefusedWith(Answer answer, string code) =>
        ShouldBeRefusedWith(answer, HttpStatusCode.BadRequest, code);

    /// <summary>Problem details of <paramref name="status" /> with a trace id and every error carrying <paramref name="code" />.</summary>
    public static string ShouldBeRefusedWith(Answer answer, HttpStatusCode status, string code)
    {
        answer.Status.ShouldBe(status, answer.Body);
        using var json = JsonDocument.Parse(answer.Body);
        var traceId = json.RootElement.GetProperty("traceId").GetString();
        traceId.ShouldNotBeNullOrWhiteSpace();
        var errors = json.RootElement.GetProperty("errors");
        errors.GetArrayLength().ShouldBeGreaterThan(0);
        errors.EnumerateArray().Select(e => e.GetProperty("code").GetString()).ShouldAllBe(c => c == code);
        return traceId!;
    }

    /// <summary>
    /// One skip warning with every field the spec names. DRK-2020 §3 Step 5: an <c>email</c> call to a host with
    /// email off is skipped with <c>ChannelNotConfigured</c>; every other channel keeps <c>ChannelNotSupported</c>.
    /// </summary>
    public static void ShouldBeSkipEntry(
        CapturedLogEntry entry,
        string notificationId,
        string templateId,
        string channel,
        string callerId,
        string reason)
    {
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Value("NotificationId").ShouldBe(notificationId);
        entry.Value("TemplateId").ShouldBe(templateId);
        entry.Value("Channel").ShouldBe(channel);
        entry.Value("CallerId").ShouldBe(callerId);
        entry.Value("Reason").ShouldBe(reason);
        entry.Value("TraceId").ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>The single skip warning of the scenario, for the last (accepted) answer.</summary>
    public CapturedLogEntry SingleSkipEntry()
    {
        var entry = SkipEntries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Value("NotificationId").ShouldBe(NotificationIdOf(LastAnswer));
        return entry;
    }

    /// <summary>One rejection entry with every field the spec names; its id is never one a caller got.</summary>
    public CapturedLogEntry SingleRejectionEntry(string code, string traceId, string callerId)
    {
        var entry = RejectionEntries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Value("Code").ShouldBe(code);
        entry.Value("CallerId").ShouldBe(callerId);
        entry.Value("TraceId").ShouldBe(traceId);
        var id = Guid.Parse(entry.Value("NotificationId").ShouldNotBeNull());
        id.ShouldNotBe(Guid.Empty);
        Answers.Select(a => a.Body).ShouldAllBe(body => !body.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));
        return entry;
    }

    /// <summary>Every captured text — message, state values, exceptions and scopes — for a personal-data search.</summary>
    public IReadOnlyList<string> AllLoggedText()
    {
        var capture = Factory.LogCapture;
        return capture.Entries
            .SelectMany(e => new[] { e.Message, e.Exception?.ToString() }
                .Concat(e.State.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))))
            .Concat(capture.Scopes.SelectMany(s =>
                s.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))))
            .OfType<string>()
            .ToArray();
    }

    #endregion

    public sealed record Credential(string? Authorization, string Claims);

    public sealed record SentCall(string Caller, string? Key, string? Body);

    public sealed record Answer(string Caller, HttpStatusCode Status, string Body)
    {
        /// <summary>The <c>Retry-After</c> header as the service sent it; null when it sent none.</summary>
        public string? RetryAfter { get; init; }
    }

    private sealed class UnknownLengthStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
