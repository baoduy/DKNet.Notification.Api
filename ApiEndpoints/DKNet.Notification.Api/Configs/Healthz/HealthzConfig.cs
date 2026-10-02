using HealthChecks.UI.Client;
using DKNet.Notification.Api.Configs.Auth;
using DKNet.Notification.Infra.Contexts;

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

        // AddDbContextCheck<DbContext>() (the base class) previously resolved nothing — only CoreDbContext is
        // registered in DI (InfraSetup.AddInfraServices) — so this check threw on every call instead of ever
        // reporting Unhealthy, hiding real DB-down states behind an unhandled exception.
        services.AddHealthChecks()
            .AddDbContextCheck<CoreDbContext>()
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

        // Public surface: status only, no check name/duration/description/exception text (R4) — anonymous by
        // design, so it must never leak dependency detail to an unauthenticated caller.
        var publicOptions = new HealthCheckOptions
        {
            AllowCachingResponses = false,
            Predicate = _ => true,
            ResponseWriter = WriteStatusOnlyResponse
        };
        endpoints.MapHealthChecks("/healthz", publicOptions).AllowAnonymous();
        endpoints.MapHealthChecks("/", publicOptions).AllowAnonymous();

        // Detailed surface: full per-check report, behind authorization only.
        var detailOptions = new HealthCheckOptions
        {
            AllowCachingResponses = false,
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        };
        var detail = endpoints.MapHealthChecks("/healthz/detail", detailOptions);

        // Only enforceable when AuthConfig actually wired UseAuthorization() — with RequireAuthorization off
        // there is no authorization middleware to evaluate the requirement (same guard as SwaggerConfig).
        if (endpoints.Services.IsConfigAdded(nameof(AuthConfig)))
        {
            detail.RequireAuthorization();
        }

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