namespace DKNet.Notification.Api.Configs.Antiforgery;

[ExcludeFromCodeCoverage]
internal static class AntiforgeryConfig
{
    #region Methods

    public static IServiceCollection AddAntiforgeryConfig(
        this IServiceCollection services,
        string cookieName = "x-csrf-cookie",
        string headerName = "x-csrf-header",
        string formFieldName = "x-csrf-field")
    {
        services.AddAntiforgery(config =>
        {
            config.Cookie.Name = cookieName;
            config.HeaderName = headerName;
            config.FormFieldName = formFieldName;

            config.Cookie.HttpOnly = true;
            config.Cookie.SameSite = SameSiteMode.Strict;
            config.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            config.SuppressXFrameOptionsHeader = false;
        });
        services.MarkConfigAdded(nameof(AntiforgeryConfig));
        return services;
    }

    public static WebApplication UseAntiforgeryConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(AntiforgeryConfig)))
        {
            app.UseMiddleware<AntiforgeryCookieMiddleware>();
            app.UseCookiePolicy();
            app.Logger.LogInformation("{Feature} enabled", nameof(AntiforgeryConfig));
        }

        return app;
    }

    #endregion
}