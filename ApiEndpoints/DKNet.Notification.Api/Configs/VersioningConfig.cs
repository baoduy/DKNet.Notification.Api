namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class VersioningConfig
{
    #region Methods

    public static IServiceCollection AddAppVersioning(this IServiceCollection services)
    {
        // AV0021 nudges toward AddApiExplorer(), which lives in the MVC-only Asp.Versioning.Mvc.ApiExplorer
        // package. This is a dknet.notification-API app documented via Microsoft.AspNetCore.OpenApi, so that package
        // doesn't apply — no fix exists here; suppress the diagnostic rather than pull in an unused MVC
        // dependency or leave the build with an unsuppressed, non-actionable warning.
#pragma warning disable AV0021
        services.AddEndpointsApiExplorer()
            .AddApiVersioning(op =>
            {
                op.DefaultApiVersion = new ApiVersion(1, 0);
                op.ReportApiVersions = true;
                op.AssumeDefaultVersionWhenUnspecified = true;
                op.ApiVersionReader = new UrlSegmentApiVersionReader();
            });
#pragma warning restore AV0021
        return services;
    }

    #endregion
}