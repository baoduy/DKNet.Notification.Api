namespace DKNet.Notification.AppServices.Templates;

/// <summary>
///     One entry of the <c>Notifications:Templates</c> settings section.
/// </summary>
public sealed class TemplateRegistration
{
    #region Properties

    /// <summary>Gets or sets the template id.</summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Gets or sets the note for template authors.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets the template's versions, one per channel.</summary>
    public IList<TemplateVersionRegistration> Versions { get; init; } = [];

    #endregion
}
