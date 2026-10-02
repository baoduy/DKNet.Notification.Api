using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.AppServices.Templates;

/// <summary>
///     The in-memory <see cref="ITemplateCatalogue" /> <see cref="TemplateCatalogueLoader" /> builds.
/// </summary>
/// <param name="templates">The checked templates, keyed by id with an ordinal comparer.</param>
internal sealed class TemplateCatalogue(IReadOnlyDictionary<string, NotificationTemplate> templates)
    : ITemplateCatalogue
{
    #region Properties

    public IReadOnlyCollection<NotificationTemplate> Templates { get; } = templates.Values.ToArray();

    #endregion

    #region Methods

    public NotificationTemplate? Find(string templateId) => templates.GetValueOrDefault(templateId);

    #endregion
}
