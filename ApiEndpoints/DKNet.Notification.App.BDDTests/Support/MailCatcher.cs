using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>How a Mailpit mail catcher takes mail. Every kind speaks TLS only.</summary>
public enum MailCatcherKind
{
    /// <summary>STARTTLS required, no sign-in, Chaos replies on: the catcher of the test run.</summary>
    StartTls,

    /// <summary>STARTTLS required, and sign-in required as <see cref="MailCatcher.SignInUser" /> only.</summary>
    SignIn,

    /// <summary>TLS from the first byte (implicit TLS) required.</summary>
    TlsOnConnect,

    /// <summary>STARTTLS required, on a certificate signed by <see cref="TestCertificateAuthority.Untrusted" />.</summary>
    Untrusted
}

/// <summary>
/// Mailpit mail catchers (DRK-2020 §3b: the tests start a Mailpit test container), one per
/// <see cref="MailCatcherKind" /> and feature (features run in parallel, see <see cref="FeatureKey" />), each started
/// on first use and disposed by <see cref="ApiHooks" /> after the run.
/// Needs Docker. Every kind but <see cref="MailCatcherKind.Untrusted" /> serves a certificate signed by
/// <see cref="TestCertificateAuthority.Trusted" />, which the service trusts only through the seam its test host
/// overrides, never through a setting. Mails are read through Mailpit's HTTP API.
/// </summary>
/// <remarks>
/// Both ports are bound to fixed host ports picked at start, so a stopped catcher comes back on the same ports and
/// a running service still reaches it. Mailpit reports no TLS flag per mail; a catcher that requires STARTTLS, or
/// TLS from the first byte, takes a mail over that encryption only, so holding the mail is the proof.
/// </remarks>
public sealed class MailCatcher : IAsyncDisposable
{
    /// <summary>The one user the <see cref="MailCatcherKind.SignIn" /> catcher accepts (DRK-2020 §5).</summary>
    public const string SignInUser = "notify-svc";

    /// <summary>The password of <see cref="SignInUser" /> (DRK-2020 §5).</summary>
    public const string SignInPassword = "Pa55-w0rd-7781";

    private const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    private static readonly ConcurrentDictionary<(string Feature, MailCatcherKind Kind), Lazy<Task<MailCatcher>>> Instances = new();

    // Host ports handed out so far: catchers of parallel features pick theirs at the same time.
    private static readonly ConcurrentDictionary<int, bool> HandedPorts = new();

    private readonly IContainer _container;
    private readonly HttpClient _api;

    private MailCatcher(MailCatcherKind kind, IContainer container, X509Certificate2 authority, int smtpHostPort, int httpHostPort)
    {
        Kind = kind;
        _container = container;
        Authority = authority;
        SmtpHostPort = smtpHostPort;
        _api = new HttpClient { BaseAddress = new Uri($"http://{container.Hostname}:{httpHostPort}") };
    }

    public MailCatcherKind Kind { get; }

    /// <summary>The test certificate authority that signed the catcher's server certificate.</summary>
    public X509Certificate2 Authority { get; }

    /// <summary>The host name the service reaches the catcher on; its server certificate names it.</summary>
    public string Host => _container.Hostname;

    /// <summary>The host port of the catcher's SMTP listener.</summary>
    public int SmtpHostPort { get; }

    public bool IsRunning => _container.State == TestcontainersStates.Running;

    /// <summary>The STARTTLS catcher of the calling feature.</summary>
    public static Task<MailCatcher> SharedAsync() => SharedAsync(MailCatcherKind.StartTls);

    /// <summary>The calling feature's catcher of <paramref name="kind" />, started on first use.</summary>
    public static Task<MailCatcher> SharedAsync(MailCatcherKind kind) =>
        Instances.GetOrAdd((FeatureKey.Current, kind), k => new Lazy<Task<MailCatcher>>(() => StartSharedAsync(k.Kind))).Value;

    /// <summary>
    /// The settings of a service with email set up to send to this catcher, without sign-in: security mode
    /// <c>Tls</c> for <see cref="MailCatcherKind.TlsOnConnect" />, <c>StartTls</c> otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, string?> EmailSettings() => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Notifications:Email:Enabled"] = "true",
        ["Notifications:Email:Sender"] = "Smtp",
        ["Notifications:Email:TimeoutSeconds"] = "30",
        ["Notifications:Email:Smtp:Host"] = Host,
        ["Notifications:Email:Smtp:Port"] = SmtpHostPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Notifications:Email:Smtp:Security"] = Kind == MailCatcherKind.TlsOnConnect ? "Tls" : "StartTls",
        ["Notifications:Email:Smtp:FromAddress"] = "notifications@drunkcoding.net",
        ["Notifications:Email:Smtp:FromName"] = "DKNet Notification"
    };

    /// <summary>Removes every mail, so a scenario starts from an empty mailbox.</summary>
    public async Task ClearAsync()
    {
        using var answer = await _api.DeleteAsync("/api/v1/messages");
        answer.EnsureSuccessStatusCode();
    }

    /// <summary>The number of mails the catcher holds, as its API reports it.</summary>
    public async Task<int> MailCountAsync()
    {
        var page = await _api.GetFromJsonAsync<MessagesPage>("/api/v1/messages");
        return page.ShouldNotBeNull("the mail catcher API answered no body").Total;
    }

    /// <summary>Every mail the catcher holds, oldest first. Read without a search, so no address goes in a URL.</summary>
    public async Task<IReadOnlyList<Mail>> MailsAsync()
    {
        var page = await _api.GetFromJsonAsync<MessagesPage>("/api/v1/messages?limit=500");
        return page.ShouldNotBeNull("the mail catcher API answered no body").Messages.Reverse().ToArray();
    }

    /// <summary>One mail in full: its HTML, its text and its attachments.</summary>
    public async Task<MailContent> MailContentAsync(string id) =>
        (await _api.GetFromJsonAsync<MailContent>($"/api/v1/message/{Uri.EscapeDataString(id)}"))
        .ShouldNotBeNull("the mail catcher API answered no body");

    /// <summary>The headers of one mail, as it was received.</summary>
    public async Task<IReadOnlyDictionary<string, string[]>> HeadersAsync(string id) =>
        (await _api.GetFromJsonAsync<Dictionary<string, string[]>>($"/api/v1/message/{Uri.EscapeDataString(id)}/headers"))
        .ShouldNotBeNull("the mail catcher API answered no body");

    /// <summary>Makes the catcher answer every recipient with <paramref name="code" /> (Mailpit Chaos).</summary>
    public async Task FailEveryRecipientWithAsync(int code) => await SetChaosAsync(code, 100);

    /// <summary>Turns every Chaos reply off.</summary>
    public async Task ClearChaosAsync() => await SetChaosAsync(451, 0);

    public async Task StopAsync()
    {
        await _container.StopAsync();
        IsRunning.ShouldBeFalse();
    }

    /// <summary>Freezes the catcher: a connection is taken, but never answered.</summary>
    public async Task PauseAsync()
    {
        await _container.PauseAsync();
        _container.State.ShouldBe(TestcontainersStates.Paused);
    }

    /// <summary>Starts the catcher again when a scenario stopped or froze it, on the same ports.</summary>
    public async Task EnsureRunningAsync()
    {
        if (_container.State == TestcontainersStates.Paused)
        {
            await _container.UnpauseAsync();
        }

        if (!IsRunning)
        {
            await _container.StartAsync();
        }

        IsRunning.ShouldBeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        _api.Dispose();
        await _container.DisposeAsync();
    }

    internal static async Task StopSharedAsync()
    {
        foreach (var instance in Instances.Values.Where(i => i.IsValueCreated))
        {
            await (await instance.Value).DisposeAsync();
        }
    }

    private async Task SetChaosAsync(int code, int probability)
    {
        if (Kind != MailCatcherKind.StartTls)
        {
            throw new InvalidOperationException("Only the STARTTLS catcher of the test run is used with Chaos replies.");
        }

        var trigger = new { ErrorCode = code, Probability = probability };
        using var answer = await _api.PutAsJsonAsync(
            "/api/v1/chaos",
            new { Sender = new { ErrorCode = 451, Probability = 0 }, Recipient = trigger, Authentication = new { ErrorCode = 535, Probability = 0 } });
        answer.EnsureSuccessStatusCode();
    }

    private static async Task<MailCatcher> StartSharedAsync(MailCatcherKind kind)
    {
        var authority = kind == MailCatcherKind.Untrusted ? TestCertificateAuthority.Untrusted : TestCertificateAuthority.Trusted;
        var (certificatePem, keyPem) = authority.IssueLocalhostPem();
        var smtpHostPort = FreePort();
        var httpHostPort = FreePort();

        var builder = new ContainerBuilder("axllent/mailpit:v1.27")
            .WithPortBinding(smtpHostPort, SmtpPort)
            .WithPortBinding(httpHostPort, HttpPort)
            .WithResourceMapping(Encoding.UTF8.GetBytes(certificatePem), "/certs/server.pem")
            .WithResourceMapping(Encoding.UTF8.GetBytes(keyPem), "/certs/server.key")
            .WithEnvironment("MP_SMTP_TLS_CERT", "/certs/server.pem")
            .WithEnvironment("MP_SMTP_TLS_KEY", "/certs/server.key")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(HttpPort).ForPath("/readyz")));
        builder = kind switch
        {
            MailCatcherKind.StartTls => builder
                .WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true")
                .WithEnvironment("MP_ENABLE_CHAOS", "true"),
            MailCatcherKind.SignIn => builder
                .WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true")
                .WithEnvironment("MP_SMTP_AUTH", $"{SignInUser}:{SignInPassword}"),
            MailCatcherKind.TlsOnConnect => builder.WithEnvironment("MP_SMTP_REQUIRE_TLS", "true"),
            MailCatcherKind.Untrusted => builder.WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no such mail catcher")
        };

        var container = builder.Build();
        await container.StartAsync();

        var catcher = new MailCatcher(kind, container, authority.Certificate, smtpHostPort, httpHostPort);
        (await catcher.MailCountAsync()).ShouldBe(0);
        return catcher;
    }

    private static int FreePort()
    {
        while (true)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (HandedPorts.TryAdd(port, true))
            {
                return port;
            }
        }
    }

    /// <summary>One address of a mail, as Mailpit reports it.</summary>
    public sealed record Mailbox(string Name, string Address);

    /// <summary>One mail as Mailpit lists it. <see cref="Bcc" /> holds the envelope recipients no header names.</summary>
    public sealed record Mail(
        string ID,
        Mailbox From,
        Mailbox[] To,
        Mailbox[]? Cc,
        Mailbox[]? Bcc,
        string Subject,
        int Attachments,
        DateTimeOffset Created);

    /// <summary>One mail in full.</summary>
    public sealed record MailContent(string ID, string HTML, string Text, JsonElement[] Attachments, JsonElement[] Inline);

    private sealed record MessagesPage(int Total, Mail[] Messages);
}
