using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DKNet.Notification.App.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace DKNet.Notification.App.Tests.Integration.Notifications;

/// <summary>
/// Spec §8, through the real host with sign-in on and no Redis: an accepted call answers 200 with its id, and the
/// caller reads that id's status back. Another caller's id and an unknown one answer alike.
/// </summary>
public sealed class NotificationStatusRouteTests : IAsyncLifetime
{
    private const string Body =
        """{"channel":"email","templateId":"account-opened","parameters":{"to":"jane@example.com","customerName":"Jane","accountNumber":"12345"}}""";

    private readonly SignedInFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task An_accepted_call_answers_200_with_its_id_and_its_status_reads_pending_or_ended()
    {
        using var response = await PostAsync("treasury-ops", "order-42");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetGuid();

        using var status = await GetAsync("treasury-ops", $"/v1/notifications/{id}");

        status.StatusCode.ShouldBe(HttpStatusCode.OK);
        status.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await status.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("notificationId").GetGuid().ShouldBe(id);
        body.GetProperty("idempotencyKey").GetString().ShouldBe("order-42");
        body.GetProperty("status").GetString().ShouldBeOneOf("pending", "success", "failed");
    }

    [Fact]
    public async Task Another_callers_id_answers_404_like_an_unknown_one()
    {
        using var response = await PostAsync("treasury-ops", "order-43");
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetGuid();

        using var other = await GetAsync("payments", $"/v1/notifications/{id}");
        using var unknown = await GetAsync("treasury-ops", $"/v1/notifications/{Guid.CreateVersion7()}");

        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherBody = await other.Content.ReadAsStringAsync();
        otherBody.ShouldContain("NOTIFICATION_NOT_FOUND");
        // Only traceId differs between two calls, so the errors are what must match.
        ErrorsOf(otherBody).ShouldBe(ErrorsOf(await unknown.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_answers_404()
    {
        using var response = await GetAsync("treasury-ops", "/v1/notifications/not-a-guid");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static string ErrorsOf(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("errors").GetRawText();

    private static void SignIn(HttpRequestMessage request, string caller)
    {
        request.Headers.Add("Authorization", "Bearer test");
        request.Headers.Add(TestAuthHandler.ClaimsHeader, $"client_id={caller};roles=notifications.send");
    }

    private async Task<HttpResponseMessage> PostAsync(string caller, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/notifications")
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", key);
        SignIn(request, caller);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetAsync(string caller, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        SignIn(request, caller);
        return await _client.SendAsync(request);
    }

    private sealed class SignedInFactory : TestApiFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("FeatureManagement:RequireAuthorization", "true");
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.ConfigureTestServices(TestAuthHandler.Register);
        }
    }
}
