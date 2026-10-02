using DKNet.AspCore.Idempotency;
using DKNet.AspCore.Idempotency.RedisStore;
using DKNet.Notification.Api.Configs.Auth;
using DKNet.Notification.Api.Configs.AzureAppConfig;
using DKNet.Notification.Api.Configs.RateLimits;
using DKNet.Notification.Api.Configs.Swagger;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class AppConfig
{
    #region Methods

    public static IServiceCollection AddAppConfig(
        this IServiceCollection services,
        FeatureOptions features,
        IConfiguration configuration)
    {
        if (features.EnableAntiforgery)
        {
            services.AddAntiforgeryConfig();
        }

        if (features.RequireAuthorization && features.EnableDemoAuthentication)
        {
            throw new InvalidOperationException(
                $"{nameof(FeatureOptions.RequireAuthorization)} and {nameof(FeatureOptions.EnableDemoAuthentication)} " +
                "cannot both be enabled: the demonstration identity is never a real caller.");
        }

        if (features.RequireAuthorization)
        {
            services.AddAuthConfig();
        }
        else if (features.EnableDemoAuthentication)
        {
            services.AddDemoAuthConfig();
        }

        if (features.EnableSwagger)
        {
            services.AddOpenApiDoc();
        }

        if (features.EnableHttps)
        {
            services.AddHttpsConfig(configuration);
        }

        if (features.EnableRateLimit)
        {
            services.AddRateLimitConfig(configuration);
        }

        if (features.EnableVersioning)
        {
            services.AddAppVersioning();
        }

        services.AddForwardedHeadersConfig(features, configuration)
            .AddSecurityHeadersConfig(features)
            .AddRequestBoundsConfig(features, configuration);

        services.AddHttpContextAccessor()
            .AddFeatureManagement();

        services.CacheConfig(configuration);

        var redisConnectionString = configuration.GetConnectionString(SharedConsts.RedisConnectionString);
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddIdempotencyWithRedisStore(
                redisConnectionString,
                o => o.ConflictHandling = IdempotentConflictHandling.ConflictResponse);
        }
        else
        {
            //InMemory store
            services.AddIdempotentKey(o => o.ConflictHandling = IdempotentConflictHandling.ConflictResponse);
        }

        return services
            .AddCrosConfig(configuration)
            .AddAllAppServices(configuration, features)
            .AddHealthzConfig(features);
    }

    public static Task UseAppConfig(this WebApplication app, Action<WebApplication>? extra = null)
    {
        // Forwarded headers and security headers run first: forwarded headers must rewrite RemoteIpAddress
        // before anything (CORS, rate limiting) makes a decision based on it, and security headers must wrap
        // everything downstream, including the global exception handler, for 200/404/500 responses alike (R5).
        app.UseAzureAppConfig()
            .UseForwardedHeadersConfig()
            .UseSecurityHeadersConfig()
            .UseAntiforgeryConfig()
            .UseCrosConfig()
            .UseHttpsConfig()
            .UseHealthzConfig();

        app.UseRouting();
        app.UseRequestBoundsConfig();
        app.UseRateLimitConfig();

        //This must be after UseRouting
        app.UseAuthConfig();

        //This is UseEndpoints
        extra?.Invoke(app);

        //These have to be after UseEndpoints.
        app.UseOpenApiDoc();

        return app.RunAsync();
    }

    #endregion
}