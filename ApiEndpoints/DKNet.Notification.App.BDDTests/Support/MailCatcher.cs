using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// One Mailpit mail catcher for the whole test run (DRK-2020 §3b: the tests start a Mailpit test container), started
/// on first use and disposed by <see cref="ApiHooks" /> after the run. Needs Docker. It speaks SMTP with STARTTLS
/// required, on a server certificate signed by a test certificate authority made here (<see cref="Authority" />);
/// the service may trust that authority only through a seam its test host overrides, never through a setting
/// (DRK-2020 R6); surface B, which adds the SMTP sender, adds that seam. Mails are read through Mailpit's HTTP API.
/// </summary>
/// <remarks>
/// Both ports are bound to fixed host ports picked at start, so a stopped catcher comes back on the same ports and
/// a running service still reaches it.
/// </remarks>
public sealed class MailCatcher : IAsyncDisposable
{
    private const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    private static readonly Lazy<Task<MailCatcher>> Instance = new(StartSharedAsync);

    private readonly IContainer _container;
    private readonly HttpClient _api;

    private MailCatcher(IContainer container, X509Certificate2 authority, int smtpHostPort, int httpHostPort)
    {
        _container = container;
        Authority = authority;
        SmtpHostPort = smtpHostPort;
        _api = new HttpClient { BaseAddress = new Uri($"http://{container.Hostname}:{httpHostPort}") };
    }

    /// <summary>The test certificate authority that signed the catcher's server certificate.</summary>
    public X509Certificate2 Authority { get; }

    /// <summary>The host name the service reaches the catcher on; its server certificate names it.</summary>
    public string Host => _container.Hostname;

    /// <summary>The host port of the catcher's SMTP listener.</summary>
    public int SmtpHostPort { get; }

    public bool IsRunning => _container.State == TestcontainersStates.Running;

    /// <summary>The shared catcher of the test run.</summary>
    public static Task<MailCatcher> SharedAsync() => Instance.Value;

    /// <summary>The settings of a service with email set up to send to this catcher over STARTTLS, without sign-in.</summary>
    public IReadOnlyDictionary<string, string?> EmailSettings() => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Notifications:Email:Enabled"] = "true",
        ["Notifications:Email:Sender"] = "Smtp",
        ["Notifications:Email:TimeoutSeconds"] = "30",
        ["Notifications:Email:Smtp:Host"] = Host,
        ["Notifications:Email:Smtp:Port"] = SmtpHostPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Notifications:Email:Smtp:Security"] = "StartTls",
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

    public async Task StopAsync()
    {
        await _container.StopAsync();
        IsRunning.ShouldBeFalse();
    }

    /// <summary>Starts the catcher again when a scenario stopped it, on the same ports.</summary>
    public async Task EnsureRunningAsync()
    {
        if (!IsRunning)
        {
            await _container.StartAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _api.Dispose();
        await _container.DisposeAsync();
        Authority.Dispose();
    }

    internal static async Task StopSharedAsync()
    {
        if (Instance.IsValueCreated)
        {
            await (await Instance.Value).DisposeAsync();
        }
    }

    private static async Task<MailCatcher> StartSharedAsync()
    {
        var (authority, certificatePem, keyPem) = CreateCertificates();
        var smtpHostPort = FreePort();
        var httpHostPort = FreePort();

        var container = new ContainerBuilder("axllent/mailpit:v1.27")
            .WithPortBinding(smtpHostPort, SmtpPort)
            .WithPortBinding(httpHostPort, HttpPort)
            .WithResourceMapping(Encoding.UTF8.GetBytes(certificatePem), "/certs/server.pem")
            .WithResourceMapping(Encoding.UTF8.GetBytes(keyPem), "/certs/server.key")
            .WithEnvironment("MP_SMTP_TLS_CERT", "/certs/server.pem")
            .WithEnvironment("MP_SMTP_TLS_KEY", "/certs/server.key")
            .WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(HttpPort).ForPath("/readyz")))
            .Build();
        await container.StartAsync();

        var catcher = new MailCatcher(container, authority, smtpHostPort, httpHostPort);
        (await catcher.MailCountAsync()).ShouldBe(0);
        return catcher;
    }

    /// <summary>A test authority, and a server certificate it signed for <c>localhost</c>, in PEM.</summary>
    private static (X509Certificate2 Authority, string CertificatePem, string KeyPem) CreateCertificates()
    {
        using var authorityKey = RSA.Create(2048);
        var authorityRequest = new CertificateRequest(
            "CN=DKNet Notification test authority",
            authorityKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        authorityRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var now = DateTimeOffset.UtcNow;
        var authority = authorityRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(7));

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var server = serverRequest.Create(authority, now.AddDays(-1), now.AddDays(6), RandomNumberGenerator.GetBytes(16));

        return (authority, server.ExportCertificatePem(), serverKey.ExportPkcs8PrivateKeyPem());
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record MessagesPage(int Total);
}
