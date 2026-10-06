using System.Text.Json.Serialization;

namespace DKNet.Notification.Client;

/// <summary>One notification to send: the channel, the registered template and its parameters.</summary>
public sealed record SendNotificationRequest(
    string Channel,
    string TemplateId,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>The id the service gave the notification.</summary>
public sealed record SendNotificationResponse([property: JsonRequired] Guid NotificationId);

/// <summary>Where one notification stands, with the idempotency key it was sent with.</summary>
public sealed record NotificationStatusResponse(
    [property: JsonRequired] Guid NotificationId,
    string? IdempotencyKey,
    [property: JsonRequired] NotificationStatus Status);

/// <summary>The public status of a notification; lower case on the wire.</summary>
[JsonConverter(typeof(NotificationStatusConverter))]
public enum NotificationStatus
{
    Pending,
    Success,
    Failed
}
