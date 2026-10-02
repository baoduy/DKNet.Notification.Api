namespace DKNet.Notification.Domains.Notifications;

/// <summary>
///     A template version filled from a call's parameters, once, before the notification is queued. Personal data:
///     never logged.
/// </summary>
/// <param name="Subject">The filled subject: raw values, each CR and LF a space, at most 998 characters.</param>
/// <param name="Body">The filled body: every value HTML-encoded.</param>
/// <param name="Format">The markup of <paramref name="Body" />.</param>
public sealed record RenderedMessage(string Subject, string Body, BodyFormat Format);
