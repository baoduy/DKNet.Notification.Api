namespace DKNet.Notification.Api.ApiEndpoints.Notifications;

/// <summary>The body of <c>GET /v1/notifications/{notificationId}</c>.</summary>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the accepting call.</param>
/// <param name="Status"><c>pending</c>, <c>success</c> or <c>failed</c>.</param>
internal sealed record NotificationStatusResponse(Guid NotificationId, string? IdempotencyKey, string Status);
