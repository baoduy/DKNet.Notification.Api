namespace DKNet.Notification.AppServices.Templates;

/// <summary>
///     Builds the <see cref="ITemplateCatalogue" /> from the settings and the template folder, once, at start-up.
/// </summary>
public static class TemplateCatalogueLoader
{
    #region Methods

    /// <summary>
    ///     Checks every registration and reads each version's file once.
    /// </summary>
    /// <param name="registrations">The <c>Notifications:Templates</c> settings section.</param>
    /// <param name="templateFolder">The release's template folder. Every version's file must sit inside it.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="InvalidOperationException">
    ///     A registration breaks a rule. The message is <c>Template '&lt;id&gt;': &lt;rule&gt;</c>.
    /// </exception>
    public static ITemplateCatalogue Load(IEnumerable<TemplateRegistration> registrations, string templateFolder) =>
        throw new NotImplementedException();

    #endregion
}
