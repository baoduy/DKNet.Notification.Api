namespace DKNet.Notification.AppServices.Notifications;

/// <summary>The status record of one notification. It holds no personal data.</summary>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the accepting call.</param>
/// <param name="Status">Where the notification stands.</param>
public sealed record NotificationStatusRecord(Guid NotificationId, string? IdempotencyKey, NotificationOutcome Status);
