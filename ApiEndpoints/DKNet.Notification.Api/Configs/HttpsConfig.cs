namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class HttpsConfig
{
    #region Fields

    /// <summary>
    ///     The HSTS preload list requires a minimum announced MaxAge of 365 days — requesting preload below that
    ///     is meaningless and browsers/preload tooling will reject the submission.
    /// </summary>
    private const int PreloadMinimumDays = 365;

    #endregion

    #region Methods

    public static IServiceCollection AddHttpsConfig(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<HstsOptions>? configureOptions = null)
    {
        if (configureOptions is not null)
        {
            services.AddHsts(configureOptions);
        }
        else
        {
            var maxAgeDays = configuration.GetValue("Https:HstsMaxAgeDays", PreloadMinimumDays);
            services.AddHsts(c =>
            {
                c.IncludeSubDomains = true;
                c.MaxAge = TimeSpan.FromDays(maxAgeDays);
                c.Preload = maxAgeDays >= PreloadMinimumDays;
            });
        }

        services.MarkConfigAdded(nameof(HttpsConfig));
        return services;
    }

    public static WebApplication UseHttpsConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(HttpsConfig)))
        {
            app.UseHsts()
                .UseHttpsRedirection();
            app.Logger.LogInformation("{Feature} enabled", nameof(HttpsConfig));
        }

        return app;
    }

    #endregion
}