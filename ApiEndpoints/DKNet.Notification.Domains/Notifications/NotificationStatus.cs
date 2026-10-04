namespace DKNet.Notification.Domains.Notifications;

/// <summary>
///     Where a <see cref="Notification" /> stands: received, then rejected, skipped or queued; a queued notification
///     is delivered or fails.
/// </summary>
public enum NotificationStatus
{
    /// <summary>The call passed its sign-in, key and body checks.</summary>
    Received,

    /// <summary>The call named no registered template.</summary>
    Rejected,

    /// <summary>The call was accepted and will not be delivered; <see cref="Notification.SkipReason" /> says why.</summary>
    Skipped,

    /// <summary>The call was rendered and waits in its replica's delivery queue.</summary>
    Queued,

    /// <summary>A delivery attempt is running.</summary>
    Delivering,

    /// <summary>An attempt failed for a transient reason; the notification waits for its next attempt.</summary>
    RetryWaiting,

    /// <summary>The provider accepted the message.</summary>
    Delivered,

    /// <summary>A permanent failure, or a transient failure on the last attempt.</summary>
    Failed
}
