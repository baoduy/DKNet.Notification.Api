using System.Diagnostics.CodeAnalysis;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>One named Teams destination of <see cref="TeamsChannelSettings" />; its name is its key there.</summary>
public sealed class TeamsDestination
{
    #region Properties

    /// <summary>
    ///     Gets or sets the Teams Workflows webhook URL: an absolute <c>https://</c> URL of at most 2,048 characters.
    ///     A secret, from a secret source only. Never logged.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1056:URI-like properties should not be strings",
        Justification = "Text, not Uri: binding a bad value to Uri throws, and a bad URL must leave only this destination not set.")]
    public string? WebhookUrl { get; set; }

    #endregion
}
