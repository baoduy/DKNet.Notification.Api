using DKNet.Notification.AppServices.Templates;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class TemplateConfig
{
    #region Fields

    private const string SectionName = "Notifications:Templates";

    #endregion

    #region Methods

    /// <summary>
    ///     Loads and checks the template catalogue once, here, so a broken catalogue stops the start-up. Bound with
    ///     <c>Get&lt;T&gt;()</c> on purpose: the catalogue never reloads on a configuration change.
    /// </summary>
    public static IServiceCollection AddTemplateConfig(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var registrations = configuration.GetSection(SectionName).Get<List<TemplateRegistration>>() ?? [];
        var catalogue = TemplateCatalogueLoader.Load(
            registrations,
            Path.Combine(environment.ContentRootPath, "Templates"));

        return services.AddSingleton(catalogue);
    }

    #endregion
}
