using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.AppServices.Templates;

/// <summary>
///     The templates registered in the release, loaded and checked once at start-up.
/// </summary>
public interface ITemplateCatalogue
{
    #region Properties

    /// <summary>Gets every registered template.</summary>
    IReadOnlyCollection<NotificationTemplate> Templates { get; }

    #endregion

    #region Methods

    /// <summary>Finds a template by its id, with an ordinal, case-sensitive match.</summary>
    /// <param name="templateId">The id the caller named.</param>
    /// <returns>The template, or <see langword="null" /> when no template has that id.</returns>
    NotificationTemplate? Find(string templateId);

    #endregion
}
