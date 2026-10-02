namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class CrosConfig
{
    #region Fields

    // DELETE deliberately excluded, and no tracing header (traceparent, X-Request-Id, ...) enumerated.
    private static readonly string[] DefaultAllowedMethods = ["GET", "POST", "PUT", "PATCH"];

    // "X-Idempotency-Key" is DKNet.AspCore.Idempotency's IdempotencyOptions.IdempotencyHeaderKey default.
    private static readonly string[] DefaultAllowedHeaders =
        ["Authorization", "Content-Type", "Accept", "X-Idempotency-Key"];

    #endregion

    #region Methods

    public static IServiceCollection AddCrosConfig(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToArray();

        if (origins.Length == 0)
        {
            return services;
        }

        // Get<string[]>() returns null when the key is absent (fall back to the secure default list) but a
        // non-null (possibly empty) array when the key is present, even as `[]` — so an explicit empty list is
        // honoured as "no methods/headers allowed" rather than silently widened back to the default.
        var methods = configuration.GetSection("Cors:AllowedMethods").Get<string[]>() ?? DefaultAllowedMethods;
        var headers = configuration.GetSection("Cors:AllowedHeaders").Get<string[]>() ?? DefaultAllowedHeaders;

        services.AddCors(c => c.AddDefaultPolicy(o => o.WithOrigins(origins).WithMethods(methods).WithHeaders(headers)));
        services.MarkConfigAdded(nameof(CrosConfig));
        return services;
    }

    public static WebApplication UseCrosConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(CrosConfig)))
        {
            app.UseCors();
            app.Logger.LogInformation("{Feature} enabled", nameof(CrosConfig));
        }

        return app;
    }

    #endregion
}