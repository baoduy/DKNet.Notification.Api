namespace DKNet.Notification.Domains.Templates;

/// <summary>
///     The markup a template version's body is written in.
/// </summary>
public enum TemplateFormat
{
    /// <summary>HTML, the format of an email version.</summary>
    Html,

    /// <summary>Markdown, the format of a Teams version.</summary>
    Markdown
}
