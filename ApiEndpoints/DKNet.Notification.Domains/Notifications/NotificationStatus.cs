namespace DKNet.Notification.Domains.Notifications;

/// <summary>
///     Where a <see cref="Notification" /> stands. This slice reaches <see cref="Received" />, <see cref="Rejected" />
///     and <see cref="Skipped" /> only; the design's delivery states come with the channel senders.
/// </summary>
public enum NotificationStatus
{
    /// <summary>The call passed its sign-in, key and body checks.</summary>
    Received,

    /// <summary>The call named no registered template.</summary>
    Rejected,

    /// <summary>The call was accepted and will not be delivered; <see cref="Notification.SkipReason" /> says why.</summary>
    Skipped
}
