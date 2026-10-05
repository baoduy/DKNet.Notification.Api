using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace DKNet.Notification.App.BDDTests.Features.Scaffold.Steps;

/// <summary>
/// DRK-1994 host for the scaffold scenarios. Deliberately NOT built on <see cref="TestApiFactoryBase" />: that base
/// swaps in a test database and overrides feature flags, while these scenarios need the API exactly as its own
/// settings files configure it, with no database anywhere.
/// <list type="bullet">
/// <item><c>Production</c> loads <c>appsettings.json</c> alone — the deployed (base) settings.</item>
/// <item><c>Development</c> layers <c>appsettings.Development.json</c> on top — the local run.</item>
/// </list>
/// With <paramref name="withValidCaller" />, <see cref="TestAuthHandler" /> stands in for a valid Entra ID token;
/// without it the real JWT bearer scheme answers, so a request with no token stays unauthenticated.
/// </summary>
public sealed class ScaffoldApiFactory(string environment, bool withValidCaller)
    : WebApplicationFactory<DKNet.Notification.Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        // A deployment refuses to start without a Redis setting (DRK-2013). The store connects lazily, and no
        // scaffold scenario sends an idempotent call, so this placeholder never opens a connection.
        builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false");

        // The bus is not started either: its Redis delivery consumer would poll the placeholder, and its stop throws
        // once a poll has failed. No scaffold scenario sends a notification.
        builder.ConfigureTestServices(services => services.Remove(services.Single(
            d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "MessageBusHostedService")));

        if (withValidCaller)
        {
            builder.ConfigureTestServices(TestAuthHandler.Register);
        }
    }

    /// <summary>
    /// An https client that never follows redirects: the base settings turn HTTPS on, and a redirect must show up
    /// as its own status rather than be silently followed.
    /// </summary>
    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
