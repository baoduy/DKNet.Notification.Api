using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>
/// DRK-2035 §3 "Sending" and "Retry rule for Teams" (brief DRK-2037 §3 rows 2–4, §6a D1–D2): the request each attempt
/// makes and the kind and status of each webhook answer or error. The sender is the real one; only the transport is a
/// fake that answers in place of the webhook. The send through an HTTPS stub runs in the BDD suite.
/// </summary>
public sealed class TeamsWebhookSenderTests
{
    private const string Webhook = "https://teams.test/workflows/7f3a/triggers/manual/paths/invoke?sig=unit-test-signature";

    private static readonly RenderedMessage Message = new("Account opened", "**Jane** opened account 12-345", BodyFormat.Markdown);

    private readonly FakeWebhook _webhook = new();
    private readonly TeamsChannelSettings _settings = new() { Enabled = true, TimeoutSeconds = 1 };

    public TeamsWebhookSenderTests() =>
        _settings.Destinations["ops-alerts"] = new TeamsDestination { WebhookUrl = Webhook };

    #region The webhook's answer

    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(204)]
    public async Task A_2xx_answer_is_a_delivery_of_one_POST_of_the_card_bytes(int status)
    {
        _webhook.Answer = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));

        (await SendAsync()).ShouldBeNull();

        var request = _webhook.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("POST");
        request.Uri.ShouldBe(new Uri(Webhook));
        request.ContentType.ShouldBe("application/json");
        request.Body.ShouldBe(TeamsCard.Serialize(Message));
    }

    [Theory]
    [InlineData(408, true, "408")]
    [InlineData(429, true, "429")]
    [InlineData(500, true, "500")]
    [InlineData(503, true, "503")]
    [InlineData(400, false, "400")]
    [InlineData(404, false, "404")]
    [InlineData(302, false, "302")]
    public async Task Each_webhook_answer_is_retried_or_not_by_its_kind(int status, bool transient, string code)
    {
        _webhook.Answer = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));

        (await SendAsync()).ShouldBe(new DeliveryFailure(transient, code));
        _webhook.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_429_with_Retry_After_asks_for_that_wait()
    {
        _webhook.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2)) }
        });

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: true, "429", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void The_transport_follows_no_redirect_and_trusts_only_the_given_roots()
    {
        using var release = GraphEndpoints.CreateTransport([]);
        var plain = release.ShouldBeOfType<SocketsHttpHandler>();
        plain.AllowAutoRedirect.ShouldBeFalse();
        plain.SslOptions.RemoteCertificateValidationCallback.ShouldBeNull();

        using var key = RSA.Create(2048);
        using var authority = new CertificateRequest("CN=Teams unit test authority", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        using var test = GraphEndpoints.CreateTransport([authority]);
        var trusting = test.ShouldBeOfType<SocketsHttpHandler>();
        trusting.AllowAutoRedirect.ShouldBeFalse();
        trusting.SslOptions.RemoteCertificateValidationCallback.ShouldNotBeNull();
    }

    #endregion

    #region Errors

    public static TheoryData<Exception> ConnectionErrors => new()
    {
        new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)),
        new HttpRequestException("lost", new IOException("The response ended prematurely.")),
        new HttpRequestException("certificate", new AuthenticationException("The remote certificate is invalid."))
    };

    [Theory]
    [MemberData(nameof(ConnectionErrors))]
    public async Task A_refused_or_lost_connection_or_a_failed_certificate_is_transient_with_no_code(Exception error)
    {
        _webhook.Answer = (_, _) => Task.FromException<HttpResponseMessage>(error);

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: true, string.Empty));
    }

    [Fact]
    public async Task An_attempt_past_the_Teams_time_limit_is_transient_with_no_code()
    {
        _webhook.Answer = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var waited = System.Diagnostics.Stopwatch.StartNew();

        (await SendAsync().WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(new DeliveryFailure(IsTransient: true, string.Empty));
        waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_host_stop_during_the_attempt_ends_it_with_no_result()
    {
        using var stop = new CancellationTokenSource();
        _webhook.Answer = async (_, token) =>
        {
            await stop.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        await Should.ThrowAsync<OperationCanceledException>(() => SendAsync(stop.Token));
    }

    [Fact]
    public async Task A_destination_with_no_webhook_fails_permanently_with_no_code_and_no_request()
    {
        (await SendAsync(destination: "finance-alerts")).ShouldBe(new DeliveryFailure(IsTransient: false, string.Empty));
        _webhook.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_notification_queued_for_email_has_no_destination_to_send_to()
    {
        var notification = Domains.Notifications.Notification.Receive("account-opened", "email", new Dictionary<string, string>(), "treasury-ops");
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        notification.Queue(recipient, Message);
        using var sender = new TeamsWebhookSender(_settings, _webhook);

        await Should.ThrowAsync<InvalidOperationException>(() => sender.SendAsync(notification, CancellationToken.None));
        _webhook.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void The_sender_needs_its_settings_and_trusted_roots()
    {
        Should.Throw<ArgumentNullException>(() => new TeamsWebhookSender(null!, new TeamsTrustedRoots([])))
            .ParamName.ShouldBe("settings");
        Should.Throw<ArgumentNullException>(() => new TeamsWebhookSender(_settings, (TeamsTrustedRoots)null!))
            .ParamName.ShouldBe("trustedRoots");
    }

    #endregion

    private async Task<DeliveryFailure?> SendAsync(CancellationToken stoppingToken = default, string destination = "ops-alerts")
    {
        var notification = Domains.Notifications.Notification.Receive("account-opened", "teams", new Dictionary<string, string>(), "treasury-ops");
        TeamsRecipient.TryCreate(destination, out var recipient).ShouldBeTrue();
        notification.Queue(recipient, Message);
        using var sender = new TeamsWebhookSender(_settings, _webhook);
        return await sender.SendAsync(notification, stoppingToken);
    }

    /// <summary>Answers in place of the webhook and records each request.</summary>
    private sealed class FakeWebhook : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Request> _requests = new();

        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));

        public IReadOnlyCollection<Request> Requests => _requests.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content.ShouldNotBeNull();
            _requests.Enqueue(new Request(
                request.Method.Method,
                request.RequestUri.ShouldNotBeNull(),
                content.Headers.ContentType?.ToString(),
                await content.ReadAsByteArrayAsync(cancellationToken)));
            return await Answer(request, cancellationToken);
        }
    }

    private sealed record Request(string Method, Uri Uri, string? ContentType, byte[] Body);
}
