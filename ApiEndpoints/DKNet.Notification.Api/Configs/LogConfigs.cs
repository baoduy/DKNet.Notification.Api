using Azure.Monitor.OpenTelemetry.AspNetCore;
using DKNet.Notification.AppServices.Delivery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class LogConfigs
{
    #region Fields

    /// <summary>The log category of the <c>Azure-Identity</c> EventSource once the Azure SDK forwards it to the logs.</summary>
    public const string AzureIdentityCategory = "Azure.Identity";

    #endregion

    #region Methods

    public static WebApplicationBuilder AddLogConfig(this WebApplicationBuilder builder, FeatureOptions features)
    {
        // The sign-in library's entries hold Microsoft Entra ID's error text, and the Azure Monitor set-up forwards
        // them to the logs: none reaches any log, in any environment (DRK-2028 R2).
        builder.Logging.AddFilter(AzureIdentityCategory, LogLevel.None);
        if (!features.EnableOpenTelemetry)
        {
#if DEBUG
            builder.Logging.AddConsole();
#endif
            return builder;
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // Console export is decided by the environment the service runs in, never by build configuration (R8):
        // a Release build run locally with Development still shows console traces/metrics, and a deployed
        // (non-Development) environment never exports either, regardless of configuration.
        var isConsoleExportEnvironment = builder.Environment.IsDevelopment();

        var otelBuilder = builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource(NotificationMetrics.DeliveryActivitySourceName);
                if (isConsoleExportEnvironment)
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(NotificationMetrics.MeterName);
                if (isConsoleExportEnvironment)
                {
                    metrics.AddConsoleExporter();
                }
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT")))
        {
            otelBuilder.UseOtlpExporter();
        }

        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetValue<string>("AzureMonitor:ConnectionString")))
        {
            otelBuilder.UseAzureMonitor();
        }

        return builder;
    }

    #endregion
}