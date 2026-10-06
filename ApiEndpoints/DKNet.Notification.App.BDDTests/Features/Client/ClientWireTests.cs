using DKNet.Notification.App.BDDTests.Features.Client.Steps;
using DKNet.Notification.Client;
using Microsoft.Extensions.Http;

namespace DKNet.Notification.App.BDDTests.Features.Client;

/// <summary>
/// DRK-2141 rules the §5 Gherkin does not reach on its own (brief DRK-2146 §6a D2, D3, D4, D6, D8, D9): the client
/// against a scripted service, through the caller's registration. Every expected value is a literal.
/// </summary>
[TestFixture]
public sealed class ClientWireTests
{
    private const string NotificationId = "8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47";

    private static readonly SendNotificationRequest Request = new(
        "email",
        "account-opened",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = "jane@example.com" });

    private readonly List<ServiceProvider> _providers = [];

    [TearDown]
    public void TearDown()
    {
        _providers.ForEach(provider => provider.Dispose());
        _providers.Clear();
    }

    #region D2 — the key, exactly as given (R1)

    [TestCase("onboard-0012345678")]
    [TestCase(" onboard 0012345678 ")]
    [TestCase("")]
    public async Task The_key_is_sent_exactly_as_given(string key)
    {
        string?[]? sent = null;
        var client = Client(request =>
        {
            sent = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.ToArray() : null;
            return Json(HttpStatusCode.OK, $$"""{"notificationId":"{{NotificationId}}"}""");
        });

        await client.SendAsync(Request, key);

        sent.ShouldBe([key]);
    }

    [Test]
    public async Task A_null_key_sends_no_header_and_the_refusal_reaches_the_caller()
    {
        var headerSent = true;
        var client = Client(request =>
        {
            headerSent = request.Headers.Contains("Idempotency-Key");
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        var refusal = await Should.ThrowAsync<NotificationApiException>(() => client.SendAsync(Request, null!));

        headerSent.ShouldBeFalse();
        refusal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Errors.ShouldBeEmpty();
    }

    #endregion

    #region D3, D4 — the status map

    [TestCase("pending", NotificationStatus.Pending)]
    [TestCase("success", NotificationStatus.Success)]
    [TestCase("failed", NotificationStatus.Failed)]
    public async Task Each_status_literal_reads_as_its_member(string literal, NotificationStatus expected)
    {
        var client = Client(_ => Json(
            HttpStatusCode.OK,
            $$"""{"notificationId":"{{NotificationId}}","idempotencyKey":"onboard-0012345678","status":"{{literal}}"}"""));

        var status = await client.GetStatusAsync(Guid.Parse(NotificationId));

        status.ShouldBe(new NotificationStatusResponse(Guid.Parse(NotificationId), "onboard-0012345678", expected));
    }

    // The service leaves a null key out of the body; an explicit null reads the same.
    [TestCase($$"""{"notificationId":"{{NotificationId}}","status":"pending"}""", TestName = "A status without its key reads as a null key")]
    [TestCase($$"""{"notificationId":"{{NotificationId}}","idempotencyKey":null,"status":"pending"}""", TestName = "A status with a null key reads as a null key")]
    public async Task A_status_without_a_key_reads_as_a_null_key(string body)
    {
        var client = Client(_ => Json(HttpStatusCode.OK, body));

        var status = await client.GetStatusAsync(Guid.Parse(NotificationId));

        status.ShouldBe(new NotificationStatusResponse(Guid.Parse(NotificationId), null, NotificationStatus.Pending));
    }

    [TestCase("\"queued\"", "Unknown notification status 'queued'.")]
    [TestCase("\"Pending\"", "Unknown notification status 'Pending'.")]
    [TestCase("1", "The notification status must be a string, not Number.")]
    [TestCase("null", "The notification status must be a string, not Null.")]
    public async Task An_unknown_status_is_refused_with_a_message_naming_it(string status, string reason)
    {
        var client = Client(_ => Json(
            HttpStatusCode.OK,
            $$"""{"notificationId":"{{NotificationId}}","idempotencyKey":"onboard-0012345678","status":{{status}}}"""));

        var refusal = await Should.ThrowAsync<NotificationApiException>(
            () => client.GetStatusAsync(Guid.Parse(NotificationId)));

        refusal.StatusCode.ShouldBe(HttpStatusCode.OK);
        refusal.Errors.ShouldBeEmpty();
        refusal.Message.ShouldBe($"The notification service answered 200 (OK) with a body the client cannot read: {reason}");
    }

    [TestCase(
        """{"notificationId":"8c7e0f3a-2b61-4d0e-9a3f-5d8b1c6e2f47","idempotencyKey":"k"}""",
        "JSON deserialization for type 'DKNet.Notification.Client.NotificationStatusResponse' was missing required properties including: 'status'.",
        TestName = "A status body without its status is refused")]
    [TestCase(
        """{"idempotencyKey":"k","status":"pending"}""",
        "JSON deserialization for type 'DKNet.Notification.Client.NotificationStatusResponse' was missing required properties including: 'notificationId'.",
        TestName = "A status body without its id is refused")]
    [TestCase(
        "this is not JSON",
        "'this is not JSON' is an invalid JSON literal. Expected the literal 'true'. Path: $ | LineNumber: 0 | BytePositionInLine: 1.",
        TestName = "A status body that is not JSON is refused")]
    public async Task A_malformed_status_body_is_refused(string body, string reason)
    {
        var client = Client(_ => Json(HttpStatusCode.OK, body));

        var refusal = await Should.ThrowAsync<NotificationApiException>(
            () => client.GetStatusAsync(Guid.Parse(NotificationId)));

        refusal.StatusCode.ShouldBe(HttpStatusCode.OK);
        refusal.Errors.ShouldBeEmpty();
        refusal.Message.ShouldBe($"The notification service answered 200 (OK) with a body the client cannot read: {reason}");
    }

    [Test]
    public async Task A_send_body_without_its_id_is_refused()
    {
        var client = Client(_ => Json(HttpStatusCode.OK, "{}"));

        var refusal = await Should.ThrowAsync<NotificationApiException>(() => client.SendAsync(Request, "k"));

        refusal.StatusCode.ShouldBe(HttpStatusCode.OK);
        refusal.Errors.ShouldBeEmpty();
        refusal.Message.ShouldBe("The notification service answered 200 (OK) with a body the client cannot read: JSON deserialization for type 'DKNet.Notification.Client.SendNotificationResponse' was missing required properties including: 'notificationId'.");
    }

    [TestCase(NotificationStatus.Pending, "\"pending\"")]
    [TestCase(NotificationStatus.Success, "\"success\"")]
    [TestCase(NotificationStatus.Failed, "\"failed\"")]
    public void A_status_writes_as_its_lower_case_literal(NotificationStatus status, string json) =>
        JsonSerializer.Serialize(status).ShouldBe(json);

    [Test]
    public void A_status_outside_the_enum_cannot_be_written() =>
        Should.Throw<JsonException>(() => JsonSerializer.Serialize((NotificationStatus)7))
            .Message.ShouldBe("Unknown notification status '7'.");

    #endregion

    #region D5, D6 — refusal bodies

    [Test]
    public async Task Every_error_entry_is_copied_exactly_as_sent()
    {
        var client = Client(_ => Json(
            HttpStatusCode.BadRequest,
            """{"status":400,"errors":[{"code":"TEMPLATE_NOT_FOUND","field":"templateId","message":"No such template."},{"code":"INVALID_REQUEST","field":"","message":" spaced "},{"code":1,"message":"numeric code"},"not an entry",{"field":"to"}]}""",
            "application/problem+json"));

        var refusal = await Should.ThrowAsync<NotificationApiException>(() => client.SendAsync(Request, "k"));

        refusal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        refusal.Errors.ShouldBe([
            new NotificationApiError { Code = "TEMPLATE_NOT_FOUND", Field = "templateId", Message = "No such template." },
            new NotificationApiError { Code = "INVALID_REQUEST", Field = "", Message = " spaced " },
            new NotificationApiError { Code = null, Field = null, Message = "numeric code" },
            new NotificationApiError { Code = null, Field = "to", Message = "" }
        ]);
        refusal.Message.ShouldBe("The notification service answered 400 (BadRequest): TEMPLATE_NOT_FOUND, INVALID_REQUEST.");
    }

    [Test]
    public async Task A_refusal_naming_an_unknown_charset_is_still_read()
    {
        var client = Client(_ =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(
                """{"errors":[{"code":"QUEUE_FULL","field":"","message":"The delivery queue is full. Try again later."}]}"""));
            content.Headers.TryAddWithoutValidation("Content-Type", "application/problem+json; charset=no-such-charset").ShouldBeTrue();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = content };
        });

        var refusal = await Should.ThrowAsync<NotificationApiException>(() => client.SendAsync(Request, "k"));

        refusal.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        refusal.Errors.ShouldBe([
            new NotificationApiError { Code = "QUEUE_FULL", Field = "", Message = "The delivery queue is full. Try again later." }
        ]);
    }

    [TestCase(HttpStatusCode.Unauthorized, "", "text/plain", TestName = "An empty 401 has no errors")]
    [TestCase(HttpStatusCode.Conflict, "   ", "text/plain", TestName = "A blank 409 has no errors")]
    [TestCase(HttpStatusCode.InternalServerError, "<html>oops</html>", "text/html", TestName = "A 500 that is not JSON has no errors")]
    [TestCase(HttpStatusCode.TooManyRequests, "[1,2]", "application/json", TestName = "A 429 array body has no errors")]
    [TestCase(HttpStatusCode.RequestEntityTooLarge, """{"title":"Too large"}""", "application/problem+json", TestName = "A 413 problem without errors has none")]
    [TestCase(HttpStatusCode.BadRequest, """{"errors":{"to":["Required."]}}""", "application/problem+json", TestName = "A 400 with an errors object has no errors")]
    public async Task A_refusal_without_an_error_list_carries_its_status_and_no_errors(
        HttpStatusCode status,
        string body,
        string mediaType)
    {
        var client = Client(_ => Json(status, body, mediaType));

        var refusal = await Should.ThrowAsync<NotificationApiException>(() => client.SendAsync(Request, "k"));

        refusal.StatusCode.ShouldBe(status);
        refusal.Errors.ShouldBeEmpty();
        refusal.Message.ShouldBe($"The notification service answered {(int)status} ({status}).");
    }

    #endregion

    #region D8 — a replayed success

    [Test]
    public async Task A_202_replay_is_a_success_with_the_same_id()
    {
        var client = Client(_ => Json(HttpStatusCode.Accepted, $$"""{"notificationId":"{{NotificationId}}"}"""));

        var sent = await client.SendAsync(Request, "onboard-0012345678");

        sent.ShouldBe(new SendNotificationResponse(Guid.Parse(NotificationId)));
    }

    #endregion

    #region D9 — transport failures pass through unchanged

    [Test]
    public async Task A_connection_failure_reaches_the_caller_unchanged()
    {
        var failure = new HttpRequestException("Connection refused");
        var client = Client(_ => throw failure);

        var raised = await Should.ThrowAsync<HttpRequestException>(() => client.SendAsync(Request, "k"));

        raised.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task A_timeout_reaches_the_caller_unchanged()
    {
        var timeout = new TaskCanceledException("The request timed out.");
        var client = Client(_ => throw timeout);

        var raised = await Should.ThrowAsync<TaskCanceledException>(
            () => client.GetStatusAsync(Guid.Parse(NotificationId)));

        // The await of a cancelled task raises a new instance of the same type; Refit's wrapper is not it.
        raised.GetType().ShouldBe(typeof(TaskCanceledException));
    }

    #endregion

    #region Registration

    [Test]
    public void Registration_refuses_a_missing_argument()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddNotificationClient(ClientScenario.StubAddress))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => services.AddNotificationClient(null!)).ParamName.ShouldBe("baseAddress");
        Should.Throw<ArgumentNullException>(() => services.AddNotificationClient(ClientScenario.StubAddress, null!))
            .ParamName.ShouldBe("messageHandlerType");
    }

    [Test]
    public void Registration_refuses_a_handler_type_that_is_not_a_delegating_handler()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddNotificationClient(ClientScenario.StubAddress, typeof(string)))
            .Message.ShouldBe("System.String is not a DelegatingHandler. (Parameter 'messageHandlerType')");
        services.ShouldBeEmpty();
    }

    #endregion

    /// <summary>The client as a caller registers it, with <paramref name="answer" /> as the service.</summary>
    private INotificationClient Client(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new PrimaryHandlerFilter(() => new ScriptedService(answer)));
        services.AddNotificationClient(ClientScenario.StubAddress);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<INotificationClient>();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}
