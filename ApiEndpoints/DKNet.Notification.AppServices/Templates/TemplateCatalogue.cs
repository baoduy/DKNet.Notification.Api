using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.AppServices.Templates;

/// <summary>
///     The in-memory <see cref="ITemplateCatalogue" /> <see cref="TemplateCatalogueLoader" /> builds.
/// </summary>
internal sealed class TemplateCatalogue : ITemplateCatalogue
{
    #region Properties

    public IReadOnlyCollection<NotificationTemplate> Templates => throw new NotImplementedException();

    #endregion

    #region Methods

    public NotificationTemplate? Find(string templateId) => throw new NotImplementedException();

    #endregion
}
