using DKNet.Notification.App.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace DKNet.Notification.App.Tests.Integration.Hosting;

/// <summary>
/// The service started with the hosting settings a scenario names — feature flags, <c>Security</c>, <c>Cors</c>,
/// <c>RateLimit</c>, <c>Https</c> — on top of its shipped <c>appsettings.json</c> and <c>appsettings.Testing.json</c>.
/// The settings go in through <c>UseSetting</c>, which reaches the configuration <c>Program.cs</c> reads while it
/// registers services, before <c>Build()</c>; no settings file is edited. Runs on TestServer, or on real Kestrel where
/// TestServer cannot show the behaviour (Kestrel's request body limit).
/// </summary>
public sealed class HostingHost : TestApiFactoryBase
{
    /// <summary>
    /// A route that only the test host maps: it reads the whole request body and answers 204. The service's own
    /// only body route caps its body at 64 KiB, so it cannot show the service-wide request body limit.
    /// </summary>
    public const string ReadBodyPath = "/test-only/read-body";

    private readonly IReadOnlyDictionary<string, string?> _settings;

    private HostingHost(IReadOnlyDictionary<string, string?> settings, bool kestrel)
    {
        _settings = settings;
        if (kestrel)
        {
            UseKestrel(0);
        }
    }

    /// <summary>
    /// Builds and starts the service with <paramref name="settings"/> (configuration keys, e.g.
    /// <c>FeatureManagement:EnableRateLimit</c>). Throws what the service's start-up threw.
    /// </summary>
    public static HostingHost Start(IReadOnlyDictionary<string, string?> settings, bool kestrel = false)
    {
        var host = new HostingHost(settings, kestrel);
        _ = host.Services;
        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        // First, so the route runs outside the service's own error handler (a start-up filter too) and Kestrel
        // answers a body over its limit itself.
        services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter, ReadBodyRoute>());
    }

    /// <summary>
    /// Sends one request through TestServer with full control of the connection — the only way to set the peer
    /// address (<see cref="ConnectionInfo.RemoteIpAddress"/>), which TestServer otherwise leaves null. Returns the
    /// request's context once the service has answered, so the client address the service saw can be read from it.
    /// </summary>
    public Task<HttpContext> SendAsync(
        string method,
        string path,
        string? peer = null,
        string host = "localhost",
        string scheme = "http",
        IReadOnlyDictionary<string, string>? headers = null) =>
        Server.SendAsync(ctx =>
        {
            ctx.Request.Method = method;
            ctx.Request.Scheme = scheme;
            ctx.Request.Host = new HostString(host);
            ctx.Request.Path = path;
            if (peer is not null)
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            }

            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            {
                ctx.Request.Headers[name] = value;
            }
        });

    /// <summary>Maps <see cref="ReadBodyPath"/> ahead of the service's own pipeline, error handler included.</summary>
    private sealed class ReadBodyRoute : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map(ReadBodyPath, route => route.Run(async context =>
            {
                await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }));

            next(app);
        };
    }
}
