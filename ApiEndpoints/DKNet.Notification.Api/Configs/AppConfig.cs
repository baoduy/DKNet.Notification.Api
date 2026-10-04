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
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (features.EnableAntiforgery)
        {
            services.AddAntiforgeryConfig();
        }

        if (features.RequireAuthorization)
        {
            services.AddAuthConfig();
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
                ConfigureIdempotency);
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            //InMemory store, local runs and test hosts only
            services.AddIdempotentKey(ConfigureIdempotency);
        }
        else
        {
            // Names the setting only, never a configuration value.
            throw new InvalidOperationException(
                $"The ConnectionStrings:{SharedConsts.RedisConnectionString} setting is missing. " +
                "Only Development and Testing run without Redis.");
        }

        services.AddTemplateConfig(configuration, environment);
        services.AddEmailConfig(configuration);
        services.AddTeamsConfig(configuration);

        return services
            .AddCrosConfig(configuration)
            .AddAllAppServices()
            .AddHealthzConfig(features);
    }

    /// <summary>
    ///     A repeat of a kept call gets its first answer, and every key is scoped by the caller id, so 2 callers never
    ///     share one. The expiry (4 hours) and the hold on a running or refused call (30 seconds) keep their defaults.
    /// </summary>
    private static void ConfigureIdempotency(IdempotencyOptions options)
    {
        options.ConflictHandling = IdempotentConflictHandling.CachedResult;
        options.IdempotencyHeaderKey = "Idempotency-Key";
        options.KeyScopeResolver = context => CallerIdentity.Resolve(context.User);
    }

    public static Task UseAppConfig(this WebApplication app, Action<WebApplication>? extra = null)
    {
        // Logged here, through the built host: an entry written while the services are registered is lost.
        app.UseEmailConfig();

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