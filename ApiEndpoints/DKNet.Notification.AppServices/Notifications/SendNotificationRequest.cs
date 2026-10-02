namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     The body of <c>POST /v1/notifications</c>. Read from JSON, so a field the caller left out arrives as
///     <see langword="null" />; <see cref="SendNotificationValidator" /> refuses it.
/// </summary>
/// <param name="Channel">Any text of 1 to 50 characters, matched without case.</param>
/// <param name="TemplateId">The registered template to send, matched exactly.</param>
/// <param name="Parameters">The template parameters, string values only. Personal data: never logged.</param>
public sealed record SendNotificationRequest(
    string Channel,
    string TemplateId,
    IReadOnlyDictionary<string, string> Parameters);
