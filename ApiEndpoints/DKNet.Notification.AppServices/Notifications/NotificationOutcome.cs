namespace DKNet.Notification.AppServices.Notifications;

/// <summary>What a caller can learn about its notification: not ended yet, delivered, or not delivered.</summary>
public enum NotificationOutcome
{
    /// <summary>Queued, being delivered, or waiting for its next attempt.</summary>
    Pending,

    /// <summary>The provider accepted the message.</summary>
    Success,

    /// <summary>Not delivered: failed, or skipped. The reason is never shown.</summary>
    Failed
}
