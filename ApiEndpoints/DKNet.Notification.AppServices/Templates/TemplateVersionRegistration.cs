using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.AppServices.Templates;

/// <summary>
///     One version of a <see cref="TemplateRegistration" /> in the settings.
/// </summary>
public sealed class TemplateVersionRegistration
{
    #region Properties

    /// <summary>Gets or sets the channel: <c>email</c> or <c>teams</c>.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the version's file, relative to the template folder. An email file's
    ///     <c>&lt;title&gt;</c> is its subject; a Teams file's front matter holds its <c>title</c>.
    /// </summary>
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets the markup the file is written in.</summary>
    public TemplateFormat? Format { get; set; }

    #endregion
}
