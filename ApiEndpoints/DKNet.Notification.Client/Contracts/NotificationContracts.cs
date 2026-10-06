namespace DKNet.Notification.Client;

/// <summary>One notification to send: the channel, the registered template and its parameters.</summary>
public sealed record SendNotificationRequest(
    string Channel,
    string TemplateId,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>The id the service gave the notification.</summary>
public sealed record SendNotificationResponse(Guid NotificationId);

/// <summary>Where one notification stands, with the idempotency key it was sent with.</summary>
public sealed record NotificationStatusResponse(
    Guid NotificationId,
    string? IdempotencyKey,
    NotificationStatus Status);

/// <summary>The public status of a notification; lower case on the wire.</summary>
public enum NotificationStatus
{
    Pending,
    Success,
    Failed
}
