namespace DKNet.Notification.Domains.Templates;

/// <summary>
///     A registered notification template: read-only, loaded once at start-up.
/// </summary>
/// <param name="TemplateId">The id a caller names: lowercase letters, digits and <c>-</c>.</param>
/// <param name="Description">A note for template authors.</param>
/// <param name="Versions">At most one version per channel.</param>
public sealed record NotificationTemplate(
    string TemplateId,
    string Description,
    IReadOnlyList<TemplateVersion> Versions);
