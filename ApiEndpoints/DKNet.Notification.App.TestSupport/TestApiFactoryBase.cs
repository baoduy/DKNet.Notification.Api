using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.TestSupport;

/// <summary>
/// Shared test host for <c>WebApplicationFactory&lt;DKNet.Notification.Api.Program&gt;</c> — the "Testing"
/// environment, captured logs and the configuration overrides both the xUnit integration suite and the Reqnroll
/// BDD suite need. Suite-specific concerns (Redis, per-scenario feature overrides, IAsyncLifetime) belong in a
/// subclass.
/// </summary>
public abstract class TestApiFactoryBase : WebApplicationFactory<DKNet.Notification.Api.Program>
{
    /// <summary>Captures log lines written by the app during a scenario/test, for asserting on log output.</summary>
    public TestLogCapture LogCapture { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(BuildFeatureOverrides()));
        builder.ConfigureServices(ConfigureTestServices);
    }

    /// <summary>
    /// Base <c>FeatureManagement</c> overrides both suites need. Override
    /// <see cref="AddFeatureOverrides" /> to extend rather than replacing this set.
    /// </summary>
    private Dictionary<string, string?> BuildFeatureOverrides()
    {
        var settings = new Dictionary<string, string?>
        {
            ["FeatureManagement:EnableSwagger"] = "false",
            ["FeatureManagement:EnableAzureAppConfig"] = "false"
        };
        AddFeatureOverrides(settings);
        return settings;
    }

    /// <summary>Extension point for a subclass's additional configuration overrides.</summary>
    protected virtual void AddFeatureOverrides(IDictionary<string, string?> settings)
    {
    }

    /// <summary>Extension point for a subclass's test service registrations.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }
}
