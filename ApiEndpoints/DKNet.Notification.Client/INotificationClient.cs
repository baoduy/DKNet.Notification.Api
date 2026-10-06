using Refit;

namespace DKNet.Notification.Client;

/// <summary>The notification service's 2 caller operations.</summary>
public interface INotificationClient
{
    /// <summary>Sends one notification with the caller's idempotency key, exactly as given.</summary>
    /// <remarks>A null key sends no <c>Idempotency-Key</c> header; the service refuses the call.</remarks>
    [Post("/v1/notifications")]
    Task<SendNotificationResponse> SendAsync(
        [Body] SendNotificationRequest request,
        [Header("Idempotency-Key")] string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Reads the status of one of the caller's notifications.</summary>
    [Get("/v1/notifications/{notificationId}")]
    Task<NotificationStatusResponse> GetStatusAsync(Guid notificationId, CancellationToken ct = default);
}
