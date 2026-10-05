using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>
/// DRK-2020 §3 Delivery, the failures of an SMTP attempt that need no TLS to reach: a refused connection, a server
/// that never answers, a greeting reply and a stop. The TLS, sign-in and delivery paths run against Mailpit in the
/// BDD suite.
/// </summary>
public sealed class SmtpEmailSenderTests : IDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);

    public SmtpEmailSenderTests() => _server.Start();

    public void Dispose() => _server.Dispose();

    private int Port => ((IPEndPoint)_server.LocalEndpoint).Port;

    [Fact]
    public async Task A_refused_connection_is_a_transient_failure_with_no_reply()
    {
        var port = Port;
        _server.Stop();

        var failure = await Sender(port).SendAsync(Queued(), CancellationToken.None);

        failure.ShouldBe(new DeliveryFailure(IsTransient: true, ReplyCode: string.Empty));
        failure!.Kind.ShouldBe("transient");
    }

    [Fact]
    public async Task A_server_that_does_not_answer_in_the_time_limit_is_a_transient_failure_with_no_reply()
    {
        // The connection is taken and never answered.
        using var silent = new CancellationTokenSource();
        var accepting = _server.AcceptTcpClientAsync(silent.Token).AsTask();
        var started = System.Diagnostics.Stopwatch.StartNew();

        var failure = await Sender(Port, timeoutSeconds: 1).SendAsync(Queued(), CancellationToken.None);

        failure.ShouldBe(new DeliveryFailure(IsTransient: true, ReplyCode: string.Empty));
        started.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.95));
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        (await accepting).Dispose();
    }

    [Theory]
    [InlineData("421 4.3.2 Service not available", true, "421")]
    [InlineData("400 4.0.0 Try again", true, "400")]
    [InlineData("499 4.0.0 Try again", true, "499")]
    [InlineData("500 5.5.1 Command unrecognized", false, "500")]
    [InlineData("554 5.3.2 No service here", false, "554")]
    public async Task A_reply_keeps_its_code_only_and_its_class_decides_the_kind(string greeting, bool transient, string code)
    {
        var answering = AnswerAsync(greeting);

        var failure = await Sender(Port).SendAsync(Queued(), CancellationToken.None);

        failure.ShouldBe(new DeliveryFailure(transient, code));
        failure!.Kind.ShouldBe(transient ? "transient" : "permanent");
        await answering;
    }

    [Fact]
    public async Task A_stop_during_the_attempt_ends_it_with_no_result()
    {
        using var stop = new CancellationTokenSource();
        var accepting = _server.AcceptTcpClientAsync(stop.Token).AsTask();
        var sending = Sender(Port).SendAsync(Queued(), stop.Token);
        using var connected = await accepting;

        await stop.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(sending);
    }

    [Fact]
    public async Task A_from_address_a_mail_header_cannot_hold_fails_permanently_with_no_reply()
    {
        // The settings rule refuses this address at start (EmailChannelSettings.BadSettings), and the recipient rule
        // refuses its kind as a "to" (DRK-2026); the sender still fails such a mail instead of throwing.
        var port = Port;
        _server.Stop();

        var failure = await Sender(port, fromAddress: "notifications.@example.com").SendAsync(Queued(), CancellationToken.None);

        failure.ShouldBe(new DeliveryFailure(IsTransient: false, ReplyCode: string.Empty));
    }

    [Fact]
    public async Task Only_a_queued_notification_can_be_sent()
    {
        var received = Domains.Notifications.Notification.Receive("account-opened", "email", new Dictionary<string, string>(), "treasury-ops", DateTimeOffset.UtcNow);

        (await Should.ThrowAsync<InvalidOperationException>(Sender(Port).SendAsync(received, CancellationToken.None)))
            .Message.ShouldBe("Only a queued notification has a message to send.");
        await Should.ThrowAsync<ArgumentNullException>(Sender(Port).SendAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void A_certificate_the_machine_trusts_is_accepted()
    {
        using var server = Authority("CN=Machine CA").Issue();

        SmtpEmailSender.IsTrusted(server, SslPolicyErrors.None, []).ShouldBeTrue();
    }

    [Fact]
    public void A_certificate_signed_by_an_extra_authority_is_accepted_only_on_a_chain_error()
    {
        using var authority = Authority("CN=Test CA");
        using var server = authority.Issue();

        SmtpEmailSender.IsTrusted(server, SslPolicyErrors.RemoteCertificateChainErrors, [authority.Certificate]).ShouldBeTrue();
        SmtpEmailSender.IsTrusted(server, SslPolicyErrors.RemoteCertificateChainErrors, []).ShouldBeFalse();
        SmtpEmailSender.IsTrusted(server, SslPolicyErrors.RemoteCertificateNameMismatch, [authority.Certificate]).ShouldBeFalse();
        SmtpEmailSender.IsTrusted(
                server,
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch,
                [authority.Certificate])
            .ShouldBeFalse();
        SmtpEmailSender.IsTrusted(null, SslPolicyErrors.RemoteCertificateChainErrors, [authority.Certificate]).ShouldBeFalse();
    }

    [Fact]
    public void A_certificate_signed_by_another_authority_is_refused()
    {
        using var trusted = Authority("CN=Test CA");
        using var other = Authority("CN=Other CA");
        using var server = other.Issue();

        SmtpEmailSender.IsTrusted(server, SslPolicyErrors.RemoteCertificateChainErrors, [trusted.Certificate]).ShouldBeFalse();
    }

    private static TestAuthority Authority(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return new TestAuthority(request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
    }

    private sealed class TestAuthority(X509Certificate2 certificate) : IDisposable
    {
        public X509Certificate2 Certificate { get; } = certificate;

        public X509Certificate2 Issue()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            return request.Create(Certificate, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(12), [1, 2, 3, 4]);
        }

        public void Dispose() => Certificate.Dispose();
    }

    private async Task AnswerAsync(string greeting)
    {
        using var client = await _server.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{greeting}\r\n"));
        // MailKit answers a refused greeting with QUIT; read until it closes.
        var buffer = new byte[256];
        while (await stream.ReadAsync(buffer) > 0)
        {
        }
    }

    private static SmtpEmailSender Sender(int port, int timeoutSeconds = 5, string fromAddress = "notifications@example.com") => new(
        new EmailChannelSettings
        {
            Enabled = true,
            TimeoutSeconds = timeoutSeconds,
            Smtp = new SmtpSenderSettings { Host = "localhost", Port = port, FromAddress = fromAddress }
        },
        new SmtpTrustedRoots([]));

    private static Domains.Notifications.Notification Queued(string to = "jane@example.com")
    {
        var notification = Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = to },
            "treasury-ops",
            DateTimeOffset.UtcNow);
        EmailRecipient.TryCreate(to, out var recipient).ShouldBeTrue();
        notification.Queue(recipient, new RenderedMessage("Your account is open", "Dear Jane", BodyFormat.Html));
        notification.StartAttempt();
        return notification;
    }
}
