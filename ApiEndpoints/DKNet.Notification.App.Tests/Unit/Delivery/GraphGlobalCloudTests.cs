using System.Collections.Concurrent;
using System.Text;
using DKNet.Notification.Api.Configs;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.Domains.Notifications;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>
/// DRK-2028 §5 <c>@unit</c> "The Graph sender uses Microsoft's global cloud" (brief DRK-2031 §3 row 1). The sender
/// and its sign-in are composed as the release composes them, from the <see cref="GraphEndpoints" /> the release's
/// email set-up registers; only the transport is a fake that answers in place of Microsoft, so no request leaves the
/// process. Every expected value is a literal from the spec: the tenant, the mailbox and Microsoft's 2 global
/// addresses.
/// </summary>
[Collection(SerialTestsCollection.Name)]
public sealed class GraphGlobalCloudTests
{
    private const string TenantId = "3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10";
    private const string Mailbox = "notify@contoso.com";

    [Fact(DisplayName = "The Graph sender uses Microsoft's global cloud")]
    public async Task The_Graph_sender_uses_Microsofts_global_cloud()
    {
        // Given the Graph sender is set up with the tenant "3f2b9c1e-…" and the mailbox "notify@contoso.com"
        var email = new EmailChannelSettings
        {
            Enabled = true,
            Sender = "Graph",
            Graph = new GraphSenderSettings
            {
                TenantId = TenantId,
                ClientId = "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41",
                Credential = "ClientSecret",
                ClientSecret = "Gr4ph-s3cret-9921",
                Mailbox = Mailbox
            }
        };
        email.BadSettings().ShouldBeEmpty();
        var endpoints = ReleaseEndpoints();
        var microsoft = new MicrosoftStandIn();
        var credential = GraphSignIn.Credential(email.Graph, endpoints, microsoft);
        var sender = new GraphEmailSender(email, credential, new HttpClient(microsoft), endpoints);

        // When it sends a notification to "jane@example.com"
        var failure = await sender.SendAsync(Queued("jane@example.com"), CancellationToken.None);

        failure.ShouldBeNull();
        // Microsoft's global cloud only, over HTTPS: no other address is called.
        microsoft.Requests.ShouldNotBeEmpty();
        microsoft.Requests.ShouldAllBe(r =>
            r.Uri.Scheme == "https" && (r.Uri.Host == "graph.microsoft.com" || r.Uri.Host == "login.microsoftonline.com"));

        // Then the send goes to Microsoft Graph's global address, on the mailbox "notify@contoso.com"
        var send = microsoft.Requests.Where(r => r.Uri.Host == "graph.microsoft.com").ShouldHaveSingleItem();
        send.Method.ShouldBe("POST");
        Unescaped(send.Uri).ShouldBe("https://graph.microsoft.com/v1.0/users/notify@contoso.com/sendMail");

        // And the sign-in goes to Microsoft Entra ID's global address, for the tenant "3f2b9c1e-…"
        var token = microsoft.Requests.Where(r => r.Method == "POST" && r.Uri.Host == "login.microsoftonline.com")
            .ShouldHaveSingleItem();
        Unescaped(token.Uri).ShouldBe("https://login.microsoftonline.com/3f2b9c1e-6a4d-4e0b-9d57-1c2f8a7e5b10/oauth2/v2.0/token");
    }

    /// <summary>
    /// The endpoints the release's email set-up registers for the sender <c>Graph</c>: Microsoft's global addresses,
    /// no extra trusted authority and no token file of its own, whatever the settings say.
    /// </summary>
    private static GraphEndpoints ReleaseEndpoints()
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Email:Enabled"] = "true",
            ["Notifications:Email:Sender"] = "Graph",
            ["Notifications:Email:Graph:TenantId"] = TenantId,
            ["Notifications:Email:Graph:ClientId"] = "7c1d4e2a-0b9f-4a63-8e15-2d6f9b3c8a41",
            ["Notifications:Email:Graph:Credential"] = "ClientSecret",
            ["Notifications:Email:Graph:ClientSecret"] = "Gr4ph-s3cret-9921",
            ["Notifications:Email:Graph:Mailbox"] = Mailbox
        }).Build();
        using var services = new ServiceCollection().AddEmailConfig(settings).BuildServiceProvider();
        var endpoints = services.GetRequiredService<GraphEndpoints>();
        endpoints.GraphAddress.ShouldBe(new Uri("https://graph.microsoft.com"));
        endpoints.AuthorityHost.ShouldBe(new Uri("https://login.microsoftonline.com/"));
        endpoints.TrustedRoots.ShouldBeEmpty();
        endpoints.ServiceAccountTokenFile.ShouldBeNull();
        return endpoints;
    }

    // The spec does not fix whether '@' in the path is escaped: compare the address as it reads.
    private static string Unescaped(Uri uri) => Uri.UnescapeDataString(uri.GetLeftPart(UriPartial.Path));

    private static Domains.Notifications.Notification Queued(string to)
    {
        var notification = Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = to },
            "treasury-ops");
        EmailRecipient.TryCreate(to, out var recipient).ShouldBeTrue();
        notification.Queue(recipient, new RenderedMessage("Your account is open", "<p>Dear Jane Tan</p>", BodyFormat.Html));
        notification.StartAttempt();
        return notification;
    }

    /// <summary>
    /// Answers in place of Microsoft: a token for the token request, 202 for <c>sendMail</c>, and 404 for anything
    /// else (such as the sign-in library's instance discovery), recording every request.
    /// </summary>
    private sealed class MicrosoftStandIn : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Request> _requests = new();

        public IReadOnlyCollection<Request> Requests => _requests.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri.ShouldNotBeNull();
            _requests.Enqueue(new Request(request.Method.Method, uri));
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (request.Method == HttpMethod.Post && path.EndsWith("/oauth2/v2.0/token", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"token_type":"Bearer","expires_in":3599,"ext_expires_in":3599,"access_token":"global-test-token"}""",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(
                request.Method == HttpMethod.Post && path.EndsWith("/sendMail", StringComparison.Ordinal)
                    ? HttpStatusCode.Accepted
                    : HttpStatusCode.NotFound));
        }
    }

    private sealed record Request(string Method, Uri Uri);
}
