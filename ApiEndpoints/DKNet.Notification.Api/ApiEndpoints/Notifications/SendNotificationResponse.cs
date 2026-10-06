namespace DKNet.Notification.Api.ApiEndpoints.Notifications;

/// <summary>The 200 body of an accepted send call, and of its replay.</summary>
/// <param name="NotificationId">The new notification's id.</param>
internal sealed record SendNotificationResponse(Guid NotificationId);
