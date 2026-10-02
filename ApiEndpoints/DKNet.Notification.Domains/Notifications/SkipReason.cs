namespace DKNet.Notification.Domains.Notifications;

/// <summary>Why an accepted <see cref="Notification" /> is not delivered.</summary>
public enum SkipReason
{
    /// <summary>No sender exists for the channel. In this release that holds for every channel.</summary>
    ChannelNotSupported
}
