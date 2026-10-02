using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace DKNet.Notification.Api.Configs;

internal sealed class RequestBoundsOptions
{
    #region Properties

    public int RequestTimeoutSeconds { get; set; } = 30;

    public long MaxRequestBodySizeBytes { get; set; } = 1 * 1024 * 1024;

    public int RequestHeadersTimeoutSeconds { get; set; } = 10;

    public static string Name => "RequestBounds";

    #endregion
}

[ExcludeFromCodeCoverage]
internal static class RequestBoundsConfig
{
    #region Methods

    public static IServiceCollection AddRequestBoundsConfig(
        this IServiceCollection services,
        FeatureOptions features,
        IConfiguration configuration)
    {
        if (!features.EnableRequestBounds)
        {
            return services;
        }

        var bounds = configuration.GetSection(RequestBoundsOptions.Name).Get<RequestBoundsOptions>()
            ?? new RequestBoundsOptions();

        services.Configure<KestrelServerOptions>(o =>
        {
            o.Limits.MaxRequestBodySize = bounds.MaxRequestBodySizeBytes;
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(bounds.RequestHeadersTimeoutSeconds);
            o.AddServerHeader = false;
        });

        services.AddRequestTimeouts(o =>
        {
            o.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(bounds.RequestTimeoutSeconds),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
            };
        });

        services.MarkConfigAdded(nameof(RequestBoundsConfig));
        return services;
    }

    public static WebApplication UseRequestBoundsConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(RequestBoundsConfig)))
        {
            app.UseRequestTimeouts();
            app.Logger.LogInformation("{Feature} enabled", nameof(RequestBoundsConfig));
        }

        return app;
    }

    #endregion
}
