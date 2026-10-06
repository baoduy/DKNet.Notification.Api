namespace DKNet.Notification.Client;

/// <summary>The notification service's 2 caller operations.</summary>
public interface INotificationClient
{
    /// <summary>Sends one notification with the caller's idempotency key, exactly as given.</summary>
    Task<SendNotificationResponse> SendAsync(
        SendNotificationRequest request,
        string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Reads the status of one of the caller's notifications.</summary>
    Task<NotificationStatusResponse> GetStatusAsync(Guid notificationId, CancellationToken ct = default);
}
