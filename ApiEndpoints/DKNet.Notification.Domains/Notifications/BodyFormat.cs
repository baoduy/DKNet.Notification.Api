namespace DKNet.Notification.Domains.Notifications;

/// <summary>The markup of a <see cref="RenderedMessage" /> body.</summary>
public enum BodyFormat
{
    /// <summary>HTML, the body of an email.</summary>
    Html,

    /// <summary>Markdown, the body of a Teams message.</summary>
    Markdown
}
