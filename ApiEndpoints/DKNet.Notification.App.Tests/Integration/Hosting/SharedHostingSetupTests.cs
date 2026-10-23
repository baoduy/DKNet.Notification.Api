using Microsoft.AspNetCore.Http;

namespace DKNet.Notification.App.Tests.Integration.Hosting;

/// <summary>
/// Spec DRK-2367 §5 (Feature: Shared API hosting setup in DKNet packages), the DKNet.Notification.Api rows, and its
/// §3 "Must stay true" items: the service answers as before once it takes its hosting setup from the DKNet packages.
/// Each scenario starts the service with its shipped settings files and turns items on or off through host settings
/// only (<see cref="HostingHost"/>). Serial: the rate-limit scenarios count requests inside a one-second window.
/// </summary>
[Collection(SerialTestsCollection.Name)]
public sealed class SharedHostingSetupTests
{
    private const string Route = "/healthz";
    private const string AllowedOrigin = "https://app.example.com";

    /// <summary>Spec §3 Must stay true: "Rate limiting never lets a caller pick its own bucket with a value it sends:
    /// … the host name" — Scenario: Requests with no client address share one bucket.</summary>
    [Fact]
    public async Task RequestsWithNoClientAddress_ShareOneBucket()
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>
        {
            ["FeatureManagement:EnableRateLimit"] = "true",
            ["RateLimit:DefaultRequestLimit"] = "2",
            ["RateLimit:TimeWindowInSeconds"] = "1"
        });
        await WarmUpAsync(host);

        var first = await host.SendAsync("GET", Route, host: "a.example");
        var second = await host.SendAsync("GET", Route, host: "b.example");
        var third = await host.SendAsync("GET", Route, host: "c.example");

        first.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        second.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        third.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Scenario Outline: Each service keeps its shipped rate limit — DKNet.Notification.Api, with the shipped
    /// <c>RateLimit</c> section (100 requests per second). The caller is one client address: the service limits
    /// requests before it signs the caller in, so a user name never picks the bucket.</summary>
    [Fact]
    public async Task KeepsItsShippedRateLimit()
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>
        {
            ["FeatureManagement:EnableRateLimit"] = "true"
        });
        await WarmUpAsync(host);

        var statuses = await SendManyAsync(host, 101, "203.0.113.9");

        statuses.Take(100).ShouldAllBe(status => status == StatusCodes.Status200OK);
        statuses[^1].ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.Notification.Api,
    /// rate limiting.</summary>
    [Fact]
    public async Task RateLimitingSwitchedOff_101RequestsWithinOneSecond_NoneRefused()
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>
        {
            ["FeatureManagement:EnableRateLimit"] = "false"
        });
        await WarmUpAsync(host);

        var statuses = await SendManyAsync(host, 101, "203.0.113.9");

        statuses.ShouldAllBe(status => status == StatusCodes.Status200OK);
    }

    /// <summary>Scenario Outline: Each service keeps the CORS headers its settings allow — DKNet.Notification.Api,
    /// Idempotency-Key. Only the origin is added; the methods and headers come from the shipped settings file.</summary>
    [Fact]
    public async Task KeepsTheCorsHeadersItsSettingsAllow()
    {
        await using var host = HostingHost.Start(AllowedOriginSettings());

        var preflight = await SendPreflightAsync(host, "POST", "Idempotency-Key");

        preflight.Response.StatusCode.ShouldBe(StatusCodes.Status204NoContent);
        preflight.Response.Headers.AccessControlAllowOrigin.ToString().ShouldBe(AllowedOrigin);
        preflight.Response.Headers.AccessControlAllowMethods.ToString().Split(',')
            .ShouldBe(["GET", "POST", "PUT", "PATCH"]);
        preflight.Response.Headers.AccessControlAllowHeaders.ToString().Split(',')
            .ShouldBe(["Authorization", "Content-Type", "Accept", "Idempotency-Key"]);
    }

    /// <summary>Brief §6a D3: the shipped header list does not allow <c>X-Idempotency-Key</c>, so a browser refuses
    /// the request: the answer allows the origin but does not list the header.</summary>
    [Fact]
    public async Task RefusesTheXIdempotencyKeyHeader()
    {
        await using var host = HostingHost.Start(AllowedOriginSettings());

        var preflight = await SendPreflightAsync(host, "POST", "X-Idempotency-Key");

        preflight.Response.Headers.AccessControlAllowOrigin.ToString().ShouldBe(AllowedOrigin);
        preflight.Response.Headers.AccessControlAllowHeaders.ToString().Split(',')
            .ShouldNotContain("X-Idempotency-Key");
    }

    /// <summary>Spec §3 Must stay true: CORS stays off when no allowed origin is configured (shipped
    /// <c>Cors:AllowedOrigins</c> is empty).</summary>
    [Fact]
    public async Task CorsStaysOff_WhenNoAllowedOriginIsConfigured()
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>());

        var preflight = await SendPreflightAsync(host, "POST", "Idempotency-Key");

        preflight.Response.Headers.AccessControlAllowOrigin.ShouldBeEmpty();
        preflight.Response.Headers.AccessControlAllowHeaders.ShouldBeEmpty();
        preflight.Response.Headers.AccessControlAllowMethods.ShouldBeEmpty();
    }

    /// <summary>Scenario Outline: Each service trusts forwarded headers from its trusted network —
    /// DKNet.Notification.Api (brief §6a D1, flag on).</summary>
    [Theory]
    [InlineData("10.1.2.3", "198.51.100.7")]
    [InlineData("203.0.113.9", "203.0.113.9")]
    public async Task TrustsForwardedHeadersFromItsTrustedNetwork(string peer, string client)
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>
        {
            ["Security:TrustedNetworks:0"] = "10.0.0.0/8"
        });

        var request = await host.SendAsync("GET", Route, peer, headers: ForwardedFor("198.51.100.7"));

        request.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse(client));
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.Notification.Api,
    /// forwarded headers.</summary>
    [Fact]
    public async Task ForwardedHeadersSwitchedOff_ServiceSeesThePeer()
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>
        {
            ["FeatureManagement:EnableForwardedHeaders"] = "false",
            ["Security:TrustedNetworks:0"] = "10.0.0.0/8"
        });

        var request = await host.SendAsync("GET", Route, "10.1.2.3", headers: ForwardedFor("198.51.100.7"));

        request.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse("10.1.2.3"));
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.Notification.Api,
    /// HTTPS.</summary>
    [Fact]
    public async Task HttpsSwitchedOff_PlainHttpIsNotRedirected_AndGetsNoHsts()
    {
        await using var host = HostingHost.Start(HttpsSettings(enabled: false));

        var plain = await host.SendAsync("GET", Route, host: "api.example.com");
        var secure = await host.SendAsync("GET", Route, host: "api.example.com", scheme: "https");

        plain.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        plain.Response.Headers.Location.ShouldBeEmpty();
        plain.Response.Headers.StrictTransportSecurity.ShouldBeEmpty();
        secure.Response.Headers.StrictTransportSecurity.ShouldBeEmpty();
    }

    /// <summary>Presence check for the HTTPS row above: the same requests with the flag on are redirected, and HTTPS
    /// gets HSTS with the shipped <c>Https:HstsMaxAgeDays</c> (365 days).</summary>
    [Fact]
    public async Task HttpsSwitchedOn_PlainHttpIsRedirected_AndHttpsGetsHsts()
    {
        await using var host = HostingHost.Start(HttpsSettings(enabled: true));

        var plain = await host.SendAsync("GET", Route, host: "api.example.com");
        var secure = await host.SendAsync("GET", Route, host: "api.example.com", scheme: "https");

        plain.Response.StatusCode.ShouldBe(StatusCodes.Status307TemporaryRedirect);
        plain.Response.Headers.Location.ToString().ShouldBe("https://api.example.com/healthz");
        secure.Response.Headers.StrictTransportSecurity.ToString()
            .ShouldBe("max-age=31536000; includeSubDomains; preload");
    }

    /// <summary>Scenario Outline: An item switched off by its feature flag changes nothing — DKNet.Notification.Api,
    /// request bounds (brief §6a D2, flag off). The whole body is read.</summary>
    [Fact]
    public async Task RequestBoundsSwitchedOff_TwoMiBBody_NotRefusedForItsSize()
    {
        using var response = await PostTwoMiBAsync(enabled: false);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>Brief §6a D2, flag on: the same body is refused by the shipped
    /// <c>RequestBounds:MaxRequestBodySizeBytes</c> (1 MiB).</summary>
    [Fact]
    public async Task RequestBoundsSwitchedOn_TwoMiBBody_RefusedWith413()
    {
        using var response = await PostTwoMiBAsync(enabled: true);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    /// <summary>Scenario Outline: Antiforgery stays off in each service — DKNet.Notification.Api. No cookie at all:
    /// neither <c>XSRF-TOKEN</c> nor any other antiforgery cookie.</summary>
    [Fact]
    public async Task AntiforgeryStaysOff_GetSetsNoXsrfTokenCookie()
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>());

        var request = await host.SendAsync("GET", Route, scheme: "https");

        request.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        request.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    /// <summary>Presence check for the antiforgery row above: with the flag on, the same GET sets a cookie.</summary>
    [Fact]
    public async Task AntiforgerySwitchedOn_GetSetsACookie()
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>
        {
            ["FeatureManagement:EnableAntiforgery"] = "true"
        });

        var request = await host.SendAsync("GET", Route, scheme: "https");

        request.Response.Headers.SetCookie.ShouldNotBeEmpty();
    }

    /// <summary>One request from a client address no scenario uses, so the host's first-request cost is not spent
    /// inside a scenario's one-second window.</summary>
    private static Task WarmUpAsync(HostingHost host) => host.SendAsync("GET", Route, "192.0.2.1");

    private static async Task<int[]> SendManyAsync(HostingHost host, int count, string peer)
    {
        var statuses = new int[count];
        for (var i = 0; i < count; i++)
        {
            statuses[i] = (await host.SendAsync("GET", Route, peer)).Response.StatusCode;
        }

        return statuses;
    }

    private static Dictionary<string, string> ForwardedFor(string client) => new() { ["X-Forwarded-For"] = client };

    private static Dictionary<string, string?> AllowedOriginSettings() => new()
    {
        ["Cors:AllowedOrigins:0"] = AllowedOrigin
    };

    private static Task<HttpContext> SendPreflightAsync(HostingHost host, string method, string header) =>
        host.SendAsync("OPTIONS", "/v1/notifications", headers: new Dictionary<string, string>
        {
            ["Origin"] = AllowedOrigin,
            ["Access-Control-Request-Method"] = method,
            ["Access-Control-Request-Headers"] = header
        });

    private static Dictionary<string, string?> HttpsSettings(bool enabled) => new()
    {
        ["FeatureManagement:EnableHttps"] = enabled ? "true" : "false",
        ["HTTPS_PORT"] = "443"
    };

    /// <summary>Posts a 2 MiB body to <see cref="HostingHost.ReadBodyPath"/> on real Kestrel: TestServer applies no
    /// Kestrel body limit. <c>Expect: 100-continue</c> lets the client read a 413 sent before the body.</summary>
    private static async Task<HttpResponseMessage> PostTwoMiBAsync(bool enabled)
    {
        await using var host = HostingHost.Start(new Dictionary<string, string?>
        {
            ["FeatureManagement:EnableRequestBounds"] = enabled ? "true" : "false"
        }, kestrel: true);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.ExpectContinue = true;

        return await client.PostAsync(HostingHost.ReadBodyPath, new ByteArrayContent(new byte[2 * 1_048_576]));
    }
}
