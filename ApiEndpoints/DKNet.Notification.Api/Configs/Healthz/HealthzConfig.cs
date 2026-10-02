namespace DKNet.Notification.Api.Configs.Healthz;

[ExcludeFromCodeCoverage]
internal static class HealthzConfig
{
    #region Methods

    public static IServiceCollection AddHealthzConfig(this IServiceCollection services, FeatureOptions features)
    {
        if (!features.EnableHealthCheck)
        {
            return services;
        }

        // Liveness only: no dependency check, so a slow or missing dependency never fails the probe.
        services.AddHealthChecks()
            .AddCheck<HealthCheckHandler>(SharedConsts.ApiName);
        services.MarkConfigAdded(nameof(HealthzConfig));
        return services;
    }

    /// <summary>
    ///     The health check endpoint will be "/healthz"
    /// </summary>
    /// <param name="endpoints"></param>
    /// <returns></returns>
    public static WebApplication UseHealthzConfig(this WebApplication endpoints)
    {
        if (!endpoints.Services.IsConfigAdded(nameof(HealthzConfig)))
        {
            return endpoints;
        }

        // Status only, no check name/duration/description/exception text — anonymous by design, so it must
        // never leak detail to an unauthenticated caller.
        endpoints.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            AllowCachingResponses = false,
            ResponseWriter = WriteStatusOnlyResponse
        }).AllowAnonymous();

        endpoints.Logger.LogInformation("{Feature} enabled", nameof(HealthzConfig));

        return endpoints;
    }

    private static Task WriteStatusOnlyResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync($$"""{"status":"{{report.Status}}"}""");
    }

    #endregion
}