namespace DKNet.Notification.Domains.Notifications;

/// <summary>Why an accepted <see cref="Notification" /> is not delivered.</summary>
public enum SkipReason
{
    /// <summary>No sender exists for the channel. In this release that holds for every channel but <c>email</c>.</summary>
    ChannelNotSupported,

    /// <summary>The channel has a sender, but this deployment has not set it up.</summary>
    ChannelNotConfigured,

    /// <summary>The template has no version for the channel.</summary>
    NoTemplateVersion,

    /// <summary>The Teams destination the call names is not set in this deployment.</summary>
    TeamsDestinationNotConfigured
}
