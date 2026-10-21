using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace DKNet.Notification.Api.Configs.AzureAppConfig;

/// <summary>
///     Extension methods for configuring Azure App Configuration integration
/// </summary>
[ExcludeFromCodeCoverage]
internal static class AzureAppConfigSetup
{
    #region Methods

    /// <summary>
    ///     Adds Azure App Configuration as a configuration source
    /// </summary>
    /// <param name="builder">The configuration builder</param>
    /// <param name="feature">The feature flag management</param>
    /// <returns>The configuration builder</returns>
    public static WebApplicationBuilder AddAzureAppConfig(this WebApplicationBuilder builder, FeatureOptions feature)
    {
        if (!feature.EnableAzureAppConfig)
        {
            return builder;
        }

        var options = builder.Configuration.GetSection(AzureAppConfigOptions.Name).Get<AzureAppConfigOptions>() ??
                      new AzureAppConfigOptions();
        var conn = builder.Configuration.GetConnectionString(options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(conn))
        {
            return builder;
        }

        builder.Configuration.AddAzureAppConfiguration(op =>
        {
            op.Connect(new Uri(conn), new DefaultAzureCredential())
                .ConfigureRefresh(c => c.RegisterAll().SetRefreshInterval(options.ResolveRefreshInterval()));

            if (options.LoadFeatureFlags)
            {
                op.UseFeatureFlags(ff =>
                {
                    if (options.ResolveFeatureFlagFilter() is { } filter)
                    {
                        ff.Select(filter);
                    }
                });
            }

            op.Select(KeyFilter.Any, options.ResolveLabel());

            // Drops feature flags the label selector also fetches, when flags are off or outside the prefix.
            op.Map(setting => ValueTask.FromResult(options.ShouldLoadSetting(setting.Key) ? setting : null));
        });

        builder.Services.MarkConfigAdded(nameof(AzureAppConfigSetup));

        return builder;
    }

    /// <summary>
    ///     Announces Azure App Configuration through <c>ILogger</c> once a host (and its logging pipeline) exists —
    ///     the source is already read at builder time in <see cref="AddAzureAppConfig" />, before any logger is
    ///     available.
    /// </summary>
    public static WebApplication UseAzureAppConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(AzureAppConfigSetup)))
        {
            app.Logger.LogInformation("{Feature} enabled", nameof(AzureAppConfigSetup));
        }

        return app;
    }

    #endregion
}