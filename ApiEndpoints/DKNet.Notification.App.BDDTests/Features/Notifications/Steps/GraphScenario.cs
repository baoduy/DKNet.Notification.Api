using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The Graph side of one scenario of <c>GraphEmailSender.feature</c>, shared by its step classes: the mail catcher,
/// the Graph stub, the token stub, the "elsewhere" stub a redirect points to, and the settings the service starts
/// with. The test host points the Graph sender at the stubs through the Graph endpoints seam only
/// (<see cref="GraphEndpoints" />), never through a setting (DRK-2028 §3 "Local run and tests").
/// </summary>
/// <remarks>
/// A token request is a <c>POST</c> to <c>/{tenant}/oauth2/v2.0/token</c> at the token stub (the Microsoft Entra ID
/// client credentials request); a send is a <c>POST</c> to <c>…/sendMail</c> at the Graph stub, whose JSON body is
/// the one of the design's "Outbound call — Microsoft Graph <c>sendMail</c>" (03-integration).
/// </remarks>
public sealed class GraphScenario(SendScenario scenario)
{
    // The spec's Graph values: the tenant and the app of its scenarios, the mailbox of "Done means" and its secret.
    public const string TenantId = "3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10";
    public const string ClientId = "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41";
    public const string Mailbox = "notify@contoso.com";
    public const string ClientSecret = "Gr4ph-s3cret-9921";

    public const string Graph = "Notifications:Email:Graph";

    /// <summary>
    /// The attempt's time limit of the scenarios that send. The spec pins none: short, so a timeout row ends soon, and
    /// long enough for a first sign-in.
    /// </summary>
    public const int TimeoutSeconds = 8;

    /// <summary>The token the token stub issues when the scenario names none.</summary>
    private const string StubToken = "graph-stub-token";

    private MailCatcher? _mailCatcher;
    private RecordingHttpStub? _graphStub;
    private RecordingHttpStub? _tokenStub;
    private RecordingHttpStub? _elsewhere;
    private string _issuedToken = StubToken;

    public MailCatcher MailCatcher => _mailCatcher.ShouldNotBeNull();

    public RecordingHttpStub GraphStub => _graphStub.ShouldNotBeNull();

    public RecordingHttpStub TokenStub => _tokenStub.ShouldNotBeNull();

    /// <summary>Stands in for <c>https://elsewhere.example</c>, the address a redirect names.</summary>
    public RecordingHttpStub Elsewhere => _elsewhere.ShouldNotBeNull();

    /// <summary>The service account token file workload identity reads; null gives it none.</summary>
    public string? ServiceAccountTokenFile { get; private set; }

    /// <summary>The access token the token stub issues to every token request it answers with a token.</summary>
    public string IssuedToken
    {
        get => _issuedToken;
        set
        {
            _issuedToken = value;
            TokenStub.DefaultReply = RecordingHttpStub.Reply.Token(value);
        }
    }

    /// <summary>Every token request the token stub received, in order.</summary>
    public IReadOnlyList<RecordingHttpStub.Request> TokenRequests =>
        TokenStub.Requests.Where(r => r.Method == "POST" && r.Path == $"/{TenantId}/oauth2/v2.0/token").ToArray();

    /// <summary>Every send the Graph stub received, in order.</summary>
    public IReadOnlyList<RecordingHttpStub.Request> Sends =>
        GraphStub.Requests.Where(r => r.Method == "POST" && r.Path.EndsWith("/sendMail", StringComparison.Ordinal)).ToArray();

    public async Task StartStubsAsync()
    {
        _mailCatcher = await MailCatcher.SharedAsync();
        await _mailCatcher.EnsureRunningAsync();
        await _mailCatcher.ClearAsync();
        _graphStub = await RecordingHttpStub.StartAsync();
        _tokenStub = await RecordingHttpStub.StartAsync();
        _elsewhere = await RecordingHttpStub.StartAsync();
        IssuedToken = StubToken;
    }

    public async Task DisposeStubsAsync()
    {
        foreach (var stub in new[] { _graphStub, _tokenStub, _elsewhere })
        {
            if (stub is not null)
            {
                await stub.DisposeAsync();
            }
        }

        if (ServiceAccountTokenFile is not null)
        {
            File.Delete(ServiceAccountTokenFile);
        }
    }

    /// <summary>Gives workload identity a service account token, in a file of this scenario's own.</summary>
    public void GiveServiceAccountToken(string token)
    {
        ServiceAccountTokenFile = Path.Combine(Path.GetTempPath(), $"sa-token-{Guid.NewGuid():N}");
        File.WriteAllText(ServiceAccountTokenFile, token);
    }

    /// <summary>
    /// Starts the service with sign-in on and Redis, the Graph sender pointed at this scenario's stubs and trusting the
    /// test authority, and workload identity reading <see cref="ServiceAccountTokenFile" />.
    /// </summary>
    public async Task StartAsync(IReadOnlyDictionary<string, string?> settings) =>
        await scenario.StartAsync(
            signIn: true,
            withRedis: true,
            settings: settings,
            graph: new GraphEndpoints(
                GraphStub.Address,
                TokenStub.Address,
                [TestCertificateAuthority.Trusted.Certificate],
                ServiceAccountTokenFile));

    /// <summary>
    /// The step names email "set up to send through the Graph stub": the start-up must have named the Graph sender,
    /// and it must be the one sender the host has (brief DRK-2031 §3 row 6), or the scenario tests nothing.
    /// </summary>
    public void ShouldSendThroughGraph()
    {
        var started = scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderStartedEvent);
        started.ShouldHaveSingleItem().Value("Sender").ShouldBe("Graph");
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderNotConfiguredEvent).ShouldBeEmpty();
        scenario.Factory.Services.GetServices<IDeliverySender>().ShouldHaveSingleItem().ShouldBeOfType<GraphEmailSender>();
    }

    /// <summary>Email on with the sender Graph and every Graph setting good, by workload identity; no SMTP setting.</summary>
    public static Dictionary<string, string?> GraphSettings() => new(StringComparer.Ordinal)
    {
        ["Notifications:Email:Enabled"] = "true",
        ["Notifications:Email:Sender"] = "Graph",
        ["Notifications:Email:TimeoutSeconds"] = "30",
        [$"{Graph}:TenantId"] = TenantId,
        [$"{Graph}:ClientId"] = ClientId,
        [$"{Graph}:Credential"] = "WorkloadIdentity",
        [$"{Graph}:Mailbox"] = Mailbox
    };

    /// <summary>
    /// The settings of the scenarios that send through the Graph stub: <see cref="GraphSettings" /> signing in with
    /// the client secret, the attempt's time limit of <see cref="TimeoutSeconds" />, and the delivery waits of
    /// 5 seconds and 30 seconds.
    /// </summary>
    public static Dictionary<string, string?> SendSettings()
    {
        var settings = GraphSettings();
        settings[$"{Graph}:Credential"] = "ClientSecret";
        settings[$"{Graph}:ClientSecret"] = ClientSecret;
        settings["Notifications:Email:TimeoutSeconds"] = TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings["Notifications:Delivery:RetryDelaysSeconds:0"] = "5";
        settings["Notifications:Delivery:RetryDelaysSeconds:1"] = "30";
        return settings;
    }

    /// <summary>The <c>message</c> of a send's JSON body.</summary>
    public static JsonElement MessageOf(RecordingHttpStub.Request send)
    {
        using var json = JsonDocument.Parse(send.Body);
        return json.RootElement.GetProperty("message").Clone();
    }

    /// <summary>Each <c>toRecipients</c> address of a send.</summary>
    public static string?[] RecipientsOf(RecordingHttpStub.Request send) =>
        MessageOf(send).GetProperty("toRecipients").EnumerateArray()
            .Select(r => r.GetProperty("emailAddress").GetProperty("address").GetString())
            .ToArray();

    /// <summary>The mailbox the send's path names: <c>/v1.0/users/{mailbox}/sendMail</c>.</summary>
    public static string MailboxOf(RecordingHttpStub.Request send)
    {
        var match = System.Text.RegularExpressions.Regex.Match(send.Path, "^/v1\\.0/users/([^/]+)/sendMail$");
        match.Success.ShouldBeTrue($"not a sendMail path: {send.Path}");
        return match.Groups[1].Value;
    }
}
