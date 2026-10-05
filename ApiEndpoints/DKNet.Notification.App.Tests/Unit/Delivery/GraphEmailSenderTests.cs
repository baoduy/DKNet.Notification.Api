using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using DKNet.Notification.AppServices.Delivery;
using Azure.Identity;
using DKNet.Notification.Domains.Notifications;
using Microsoft.Identity.Client;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>
/// DRK-2028 §3 "Sending" and "Retry rule for Graph" (brief DRK-2030 §3 rows 2–5, §8): the kind and status of each
/// token and Graph answer, the request the send makes, the sign-in's tenant and app, and the sign-in library's
/// throttling after a token 429. The sender and its sign-in are the real ones; only the transport is a fake that
/// answers in place of Entra ID and Graph. The send and sign-in through HTTPS stubs run in the BDD suite.
/// </summary>
[Collection(SerialTestsCollection.Name)]
public sealed class GraphEmailSenderTests
{
    private const string TenantId = "3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10";
    private const string ClientId = "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41";
    private const string TokenPath = "/3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10/oauth2/v2.0/token";
    private const string SendPath = "/v1.0/users/notify@contoso.com/sendMail";

    private readonly FakeMicrosoft _microsoft = new();

    // A sign-in address of each test's own: the sign-in library keeps its throttling for the whole process, keyed by
    // the address, so a token 429 of one test would throttle the next one.
    private readonly GraphEndpoints _endpoints = new(
        new Uri("https://graph.test"),
        new Uri($"https://login-{Guid.NewGuid():N}.test/"),
        [],
        serviceAccountTokenFile: null);

    #region The send's answer

    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(204)]
    public async Task A_2xx_answer_from_Graph_is_a_delivery(int status)
    {
        _microsoft.SendAnswer = () => new HttpResponseMessage((HttpStatusCode)status);

        (await SendAsync()).ShouldBeNull();
        _microsoft.Sends.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(408, true, "408")]
    [InlineData(429, true, "429")]
    [InlineData(500, true, "500")]
    [InlineData(502, true, "502")]
    [InlineData(503, true, "503")]
    [InlineData(504, true, "504")]
    [InlineData(400, false, "400")]
    [InlineData(401, false, "401")]
    [InlineData(403, false, "403")]
    [InlineData(404, false, "404")]
    [InlineData(409, false, "409")]
    [InlineData(499, false, "499")]
    [InlineData(301, false, "301")]
    [InlineData(302, false, "302")]
    public async Task Each_Graph_answer_is_retried_or_not_by_its_kind(int status, bool transient, string code)
    {
        _microsoft.SendAnswer = () => new HttpResponseMessage((HttpStatusCode)status);

        (await SendAsync()).ShouldBe(new DeliveryFailure(transient, code));
    }

    [Fact]
    public async Task A_Graph_redirect_is_not_followed()
    {
        _microsoft.SendAnswer = () => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://elsewhere.example/") }
        };
        using var transport = _endpoints.CreateTransport();

        transport.ShouldBeOfType<SocketsHttpHandler>().AllowAutoRedirect.ShouldBeFalse();
        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: false, "302"));
    }

    [Fact]
    public void The_release_transport_trusts_the_machine_authorities_only()
    {
        using var transport = GraphEndpoints.Global.CreateTransport();

        transport.ShouldBeOfType<SocketsHttpHandler>().SslOptions.RemoteCertificateValidationCallback.ShouldBeNull();
    }

    [Fact]
    public void A_test_transport_trusts_its_extra_authority_and_still_refuses_a_name_mismatch()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=Graph test authority", key, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var endpoints = new GraphEndpoints(_endpoints.GraphAddress, _endpoints.AuthorityHost, [root], serviceAccountTokenFile: null);
        using var transport = endpoints.CreateTransport();

        var trust = transport.ShouldBeOfType<SocketsHttpHandler>().SslOptions.RemoteCertificateValidationCallback.ShouldNotBeNull();

        trust(this, root, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
        trust(this, root, null, System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch).ShouldBeFalse();
    }

    [Fact]
    public async Task A_Graph_429_waits_the_retry_after_it_names()
    {
        _microsoft.SendAnswer = () => WithRetryAfter(new HttpResponseMessage(HttpStatusCode.TooManyRequests), seconds: 2);

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: true, "429", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task A_Graph_503_keeps_the_configured_wait_even_with_a_retry_after()
    {
        _microsoft.SendAnswer = () => WithRetryAfter(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), seconds: 2);

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: true, "503"));
    }

    [Fact]
    public async Task A_refused_or_lost_send_is_a_transient_failure_with_no_status()
    {
        _microsoft.SendAnswer = () => throw new HttpRequestException("Connection refused");

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: true, string.Empty));
    }

    [Fact]
    public async Task A_send_with_no_answer_in_the_time_limit_is_a_transient_failure_with_no_status()
    {
        _microsoft.SendDelay = TimeSpan.FromSeconds(30);

        (await SendAsync(timeoutSeconds: 1)).ShouldBe(new DeliveryFailure(IsTransient: true, string.Empty));
        _microsoft.Sends.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_slow_token_and_a_slow_send_share_one_time_limit()
    {
        // Each step alone is under the 2 s limit; together they pass it.
        _microsoft.TokenDelay = TimeSpan.FromMilliseconds(1200);
        _microsoft.SendDelay = TimeSpan.FromMilliseconds(1200);

        (await SendAsync(timeoutSeconds: 2)).ShouldBe(new DeliveryFailure(IsTransient: true, string.Empty));
    }

    [Fact]
    public async Task A_host_stop_ends_the_attempt_with_no_result()
    {
        _microsoft.SendDelay = TimeSpan.FromSeconds(30);
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => SendAsync(stoppingToken: stopping.Token));
    }

    [Fact]
    public async Task An_error_no_attempt_should_meet_is_left_to_the_worker()
    {
        _microsoft.SendAnswer = () => throw new InvalidOperationException("unexpected");

        await Should.ThrowAsync<InvalidOperationException>(() => SendAsync());
    }

    [Fact]
    public async Task A_notification_that_was_not_queued_is_refused()
    {
        var notification = Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = "jane@example.com" },
            "treasury-ops");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Sender(Settings()).SendAsync(notification, CancellationToken.None));
        _microsoft.Requests.ShouldBeEmpty();
    }

    #endregion

    #region The token's answer

    [Theory]
    [InlineData(408, true, "408")]
    [InlineData(429, true, "429")]
    [InlineData(500, true, "500")]
    [InlineData(503, true, "503")]
    [InlineData(400, false, "400")]
    [InlineData(401, false, "401")]
    public async Task Each_sign_in_answer_is_retried_or_not_by_its_kind(int status, bool transient, string code)
    {
        _microsoft.Token = () => TokenError((HttpStatusCode)status);

        (await SendAsync()).ShouldBe(new DeliveryFailure(transient, code));
        _microsoft.TokenRequests.Count.ShouldBe(1);
        _microsoft.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sign_in_429_waits_the_retry_after_it_names()
    {
        _microsoft.Token = () => WithRetryAfter(TokenError(HttpStatusCode.TooManyRequests), seconds: 7);

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: true, "429", TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public async Task A_throttled_sign_in_after_a_token_429_is_transient_with_the_same_status_and_no_new_request()
    {
        _microsoft.Token = () => WithRetryAfter(TokenError(HttpStatusCode.TooManyRequests), seconds: 60);
        var sender = Sender(Settings());

        await sender.SendAsync(Queued(), CancellationToken.None);
        var throttled = await sender.SendAsync(Queued(), CancellationToken.None);

        throttled.ShouldNotBeNull();
        throttled.IsTransient.ShouldBeTrue();
        throttled.ReplyCode.ShouldBe("429");
        // The throttled answer keeps the 429's headers, so the next attempt still waits the retry-after it named.
        throttled.RetryAfter.ShouldBe(TimeSpan.FromSeconds(60));
        _microsoft.TokenRequests.Count.ShouldBe(1);
    }

    [Fact]
    public void A_sign_in_429_with_no_headers_keeps_the_configured_wait() =>
        GraphEmailSender.FailureOf(new MsalServiceException("too_many_requests", "Slow down.", 429, innerException: null))
            .ShouldBe(new DeliveryFailure(IsTransient: true, "429"));

    [Fact]
    public void A_sign_in_error_with_no_status_and_no_network_cause_is_left_to_the_worker() =>
        GraphEmailSender.FailureOf(new AuthenticationFailedException(
                "failed",
                new MsalServiceException("unknown_error", "Something failed.", 0, innerException: null)))
            .ShouldBeNull();

    [Fact]
    public async Task A_refused_sign_in_is_a_transient_failure_with_no_status()
    {
        _microsoft.Token = () => throw new HttpRequestException("Connection refused");

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: true, string.Empty));
        _microsoft.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_is_reused_by_the_next_attempt()
    {
        var sender = Sender(Settings());

        (await sender.SendAsync(Queued(), CancellationToken.None)).ShouldBeNull();
        (await sender.SendAsync(Queued(), CancellationToken.None)).ShouldBeNull();

        _microsoft.TokenRequests.Count.ShouldBe(1);
        _microsoft.Sends.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Workload_identity_with_no_service_account_token_fails_at_once()
    {
        Environment.GetEnvironmentVariable("AZURE_FEDERATED_TOKEN_FILE").ShouldBeNullOrEmpty();
        var settings = Settings();
        settings.Graph.Credential = "WorkloadIdentity";

        (await SendAsync(settings)).ShouldBe(new DeliveryFailure(IsTransient: false, string.Empty));
        _microsoft.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Workload_identity_signs_in_with_the_service_account_token_and_not_the_secret()
    {
        var tokenFile = Path.Combine(Path.GetTempPath(), $"sa-token-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tokenFile, "sa-token-4417");
        try
        {
            var settings = Settings();
            settings.Graph.Credential = "workloadidentity";
            var endpoints = new GraphEndpoints(_endpoints.GraphAddress, _endpoints.AuthorityHost, [], tokenFile);

            (await SendAsync(settings, endpoints)).ShouldBeNull();

            var form = HttpUtility.ParseQueryString(_microsoft.TokenRequests.ShouldHaveSingleItem().Body);
            form["client_assertion"].ShouldBe("sa-token-4417");
            form["client_id"].ShouldBe(ClientId);
            form["client_secret"].ShouldBeNull();
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Theory]
    [InlineData("{3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10}", "{7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41}")]
    [InlineData("3f2b9c1e6a4d4e0b9d571c2f8a7e5b10", "7c1d4e2a0b9f4a638e152d6f9b3c8a41")]
    [InlineData("3F2B9C1E-6A4D-4E0B-9D57-1C2F8A7E5B10", "7C1D4E2A-0B9F-4A63-8E15-2D6F9B3C8A41")]
    public async Task The_sign_in_uses_the_D_form_of_the_tenant_and_client_ids(string tenantId, string clientId)
    {
        var settings = Settings();
        settings.Graph.TenantId = tenantId;
        settings.Graph.ClientId = clientId;
        settings.BadSettings().ShouldBeEmpty();

        (await SendAsync(settings)).ShouldBeNull();

        var token = _microsoft.TokenRequests.ShouldHaveSingleItem();
        token.Uri.ToString().ShouldBe($"{_endpoints.AuthorityHost}3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10/oauth2/v2.0/token");
        HttpUtility.ParseQueryString(token.Body)["client_id"].ShouldBe(ClientId);
    }

    [Fact]
    public async Task The_client_secret_signs_in_as_the_app_for_Microsoft_Graph()
    {
        (await SendAsync()).ShouldBeNull();

        var form = HttpUtility.ParseQueryString(_microsoft.TokenRequests.ShouldHaveSingleItem().Body);
        form["grant_type"].ShouldBe("client_credentials");
        form["client_id"].ShouldBe(ClientId);
        form["client_secret"].ShouldBe("Gr4ph-s3cret-9921");
        form["scope"].ShouldBe("https://graph.microsoft.com/.default");
        // A test authority gets no instance discovery: the token request and the send are the only calls.
        _microsoft.Requests.Select(r => $"{r.Method} {r.Uri.Host}").ShouldBe([$"POST {_endpoints.AuthorityHost.Host}", "POST graph.test"]);
    }

    [Fact]
    public async Task Workload_identity_at_a_test_authority_gets_no_instance_discovery()
    {
        var tokenFile = Path.Combine(Path.GetTempPath(), $"sa-token-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tokenFile, "sa-token-4417");
        try
        {
            var settings = Settings();
            settings.Graph.Credential = "WorkloadIdentity";
            var endpoints = new GraphEndpoints(_endpoints.GraphAddress, _endpoints.AuthorityHost, [], tokenFile);

            (await SendAsync(settings, endpoints)).ShouldBeNull();

            _microsoft.Requests.Select(r => $"{r.Method} {r.Uri.Host}").ShouldBe([$"POST {_endpoints.AuthorityHost.Host}", "POST graph.test"]);
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    public async Task The_sign_in_writes_no_request_log_and_starts_no_Azure_activity()
    {
        // The Azure SDK writes its request log to the Azure-Core EventSource and its activities to DiagnosticListeners:
        // listen to both while a token request fails with an error text.
        _microsoft.Token = () => TokenError(HttpStatusCode.BadRequest);
        var events = new ConcurrentQueue<string>();
        using var logs = new Azure.Core.Diagnostics.AzureEventSourceListener(
            (e, message) => events.Enqueue($"{e.EventSource.Name}: {message}"),
            System.Diagnostics.Tracing.EventLevel.Verbose);
        using var activities = new AzureActivityListener();

        (await SendAsync()).ShouldBe(new DeliveryFailure(IsTransient: false, "400"));

        // The listener works: the sign-in library's own events reach it.
        events.ShouldContain(e => e.StartsWith("Azure-Identity: ", StringComparison.Ordinal));
        events.ShouldNotContain(e => e.StartsWith("Azure-Core: ", StringComparison.Ordinal));
        activities.Started.ShouldBeEmpty();
    }

    [Fact]
    public void The_sign_in_refuses_missing_settings_or_endpoints()
    {
        Should.Throw<ArgumentNullException>(() => GraphSignIn.Credential(null!, _endpoints, _microsoft))
            .ParamName.ShouldBe("settings");
        Should.Throw<ArgumentNullException>(() => GraphSignIn.Credential(Settings().Graph, null!, _microsoft))
            .ParamName.ShouldBe("endpoints");
    }

    [Fact]
    public async Task The_sender_refuses_missing_settings_endpoints_or_notification()
    {
        var credential = GraphSignIn.Credential(Settings().Graph, _endpoints, _microsoft);
        using var http = new HttpClient(_microsoft, disposeHandler: false);

        Should.Throw<ArgumentNullException>(() => new GraphEmailSender(null!, credential, http, _endpoints))
            .ParamName.ShouldBe("email");
        Should.Throw<ArgumentNullException>(() => new GraphEmailSender(Settings(), credential, http, null!))
            .ParamName.ShouldBe("endpoints");
        (await Should.ThrowAsync<ArgumentNullException>(() => Sender(Settings()).SendAsync(null!, CancellationToken.None)))
            .ParamName.ShouldBe("notification");
    }

    #endregion

    #region The send's request

    [Fact]
    public async Task The_send_is_one_sendMail_on_the_mailbox_with_the_token_the_sign_in_issued()
    {
        (await SendAsync()).ShouldBeNull();

        var send = _microsoft.Sends.ShouldHaveSingleItem();
        send.Method.ShouldBe("POST");
        Uri.UnescapeDataString(send.Uri.GetLeftPart(UriPartial.Path)).ShouldBe("https://graph.test/v1.0/users/notify@contoso.com/sendMail");
        send.Authorization.ShouldBe("Bearer unit-test-token");
    }

    [Fact]
    public async Task The_send_body_holds_the_subject_the_HTML_body_and_the_one_recipient_only()
    {
        (await SendAsync()).ShouldBeNull();

        var sent = System.Text.Json.Nodes.JsonNode.Parse(_microsoft.Sends.ShouldHaveSingleItem().Body);
        var expected = System.Text.Json.Nodes.JsonNode.Parse(
            """{"message":{"subject":"Your account is open","body":{"contentType":"HTML","content":"<p>Dear Jane Tan</p>"},"toRecipients":[{"emailAddress":{"address":"jane@example.com"}}]}}""");
        System.Text.Json.Nodes.JsonNode.DeepEquals(sent, expected).ShouldBeTrue(sent!.ToJsonString());
    }

    [Fact]
    public async Task A_disposed_sender_disposes_its_client()
    {
        var http = new HttpClient(_microsoft);
        var sender = new GraphEmailSender(Settings(), GraphSignIn.Credential(Settings().Graph, _endpoints, _microsoft), http, _endpoints);

        sender.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(() => http.GetAsync(new Uri("https://graph.test/")));
    }

    #endregion

    #region Helpers

    private static EmailChannelSettings Settings(int timeoutSeconds = 30) => new()
    {
        Enabled = true,
        Sender = "Graph",
        TimeoutSeconds = timeoutSeconds,
        Graph = new GraphSenderSettings
        {
            TenantId = TenantId,
            ClientId = ClientId,
            Credential = "ClientSecret",
            ClientSecret = "Gr4ph-s3cret-9921",
            Mailbox = "notify@contoso.com"
        }
    };

    private GraphEmailSender Sender(EmailChannelSettings settings, GraphEndpoints? endpoints = null)
    {
        endpoints ??= _endpoints;
        return new GraphEmailSender(
            settings,
            GraphSignIn.Credential(settings.Graph, endpoints, _microsoft),
            new HttpClient(_microsoft, disposeHandler: false),
            endpoints);
    }

    private Task<DeliveryFailure?> SendAsync(
        EmailChannelSettings? settings = null,
        GraphEndpoints? endpoints = null,
        int timeoutSeconds = 30,
        CancellationToken stoppingToken = default) =>
        Sender(settings ?? Settings(timeoutSeconds), endpoints).SendAsync(Queued(), stoppingToken);

    private static Domains.Notifications.Notification Queued()
    {
        var notification = Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = "jane@example.com" },
            "treasury-ops");
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        notification.Queue(recipient, new RenderedMessage("Your account is open", "<p>Dear Jane Tan</p>", BodyFormat.Html));
        notification.StartAttempt();
        return notification;
    }

    private static HttpResponseMessage TokenError(HttpStatusCode status) => new(status)
    {
        Content = new StringContent(
            """{"error":"temporarily_unavailable","error_description":"Bad request, trace 7f3a-9921"}""",
            Encoding.UTF8,
            "application/json")
    };

    private static HttpResponseMessage WithRetryAfter(HttpResponseMessage response, int seconds)
    {
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    /// <summary>
    /// Answers in place of Entra ID and Graph: a token for the token request and 202 for <c>sendMail</c> unless a test
    /// scripts otherwise, and 404 for anything else, recording every request.
    /// </summary>
    private sealed class FakeMicrosoft : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Request> _requests = new();

        public Func<HttpResponseMessage> Token { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"token_type":"Bearer","expires_in":3599,"ext_expires_in":3599,"access_token":"unit-test-token"}""",
                Encoding.UTF8,
                "application/json")
        };

        public Func<HttpResponseMessage> SendAnswer { get; set; } = () => new HttpResponseMessage(HttpStatusCode.Accepted);

        public TimeSpan TokenDelay { get; set; }

        public TimeSpan SendDelay { get; set; }

        public IReadOnlyCollection<Request> Requests => _requests.ToArray();

        public IReadOnlyList<Request> TokenRequests => Requests.Where(r => r.Uri.AbsolutePath == TokenPath).ToArray();

        public IReadOnlyList<Request> Sends =>
            Requests.Where(r => Uri.UnescapeDataString(r.Uri.AbsolutePath) == SendPath).ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri.ShouldNotBeNull();
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new Request(request.Method.Method, uri, request.Headers.Authorization?.ToString(), body);
            _requests.Enqueue(recorded);
            if (request.Method == HttpMethod.Post && uri.AbsolutePath == TokenPath)
            {
                await Task.Delay(TokenDelay, cancellationToken);
                return Token();
            }

            if (request.Method == HttpMethod.Post && Uri.UnescapeDataString(uri.AbsolutePath) == SendPath)
            {
                await Task.Delay(SendDelay, cancellationToken);
                return SendAnswer();
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed record Request(string Method, Uri Uri, string? Authorization, string Body);

    /// <summary>Records every activity an <c>Azure.*</c> DiagnosticListener starts while it lives.</summary>
    private sealed class AzureActivityListener : IObserver<System.Diagnostics.DiagnosticListener>, IDisposable
    {
        private readonly ConcurrentQueue<string> _started = new();
        private readonly List<IDisposable> _subscriptions = [];

        public AzureActivityListener() => _subscriptions.Add(System.Diagnostics.DiagnosticListener.AllListeners.Subscribe(this));

        public IReadOnlyCollection<string> Started => _started.ToArray();

        public void OnNext(System.Diagnostics.DiagnosticListener value)
        {
            if (value.Name.StartsWith("Azure.", StringComparison.Ordinal))
            {
                lock (_subscriptions)
                {
                    _subscriptions.Add(value.Subscribe(new ActivityObserver(_started)));
                }
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            lock (_subscriptions)
            {
                _subscriptions.ForEach(s => s.Dispose());
            }
        }

        private sealed class ActivityObserver(ConcurrentQueue<string> started) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Key.EndsWith(".Start", StringComparison.Ordinal))
                {
                    started.Enqueue(value.Key);
                }
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }
        }
    }

    #endregion
}
