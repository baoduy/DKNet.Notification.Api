namespace DKNet.Notification.Domains.Templates;

/// <summary>
///     One channel's version of a <see cref="NotificationTemplate" />, read from the release at start-up.
/// </summary>
/// <param name="Channel">The channel this version is for: <c>email</c> or <c>teams</c>.</param>
/// <param name="File">The version's file, relative to the template folder.</param>
/// <param name="Format">The markup the body is written in.</param>
/// <param name="Subject">The email subject, from the file's <c>&lt;title&gt;</c>. Email versions only.</param>
/// <param name="Title">The message title, from the file's front matter. Teams versions only.</param>
/// <param name="Body">The text of <paramref name="File" /> without a Teams version's front matter, read at start-up.</param>
public sealed record TemplateVersion(
    string Channel,
    string File,
    TemplateFormat Format,
    string? Subject,
    string? Title,
    string Body);
