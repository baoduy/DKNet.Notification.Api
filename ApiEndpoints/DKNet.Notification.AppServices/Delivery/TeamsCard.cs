using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The Teams message of a rendered notification: 1 Adaptive Card with an optional title block and one Markdown
///     text block, written by the JSON serializer. The bytes measured are the bytes posted.
/// </summary>
public static class TeamsCard
{
    #region Fields

    /// <summary>The largest Teams message the service posts: 28,672 bytes (28 × 1,024).</summary>
    public const int MaxBytes = 28_672;

    #endregion

    #region Methods

    /// <summary>Writes the Teams message of <paramref name="message" />.</summary>
    /// <param name="message">The filled title and Markdown body.</param>
    /// <returns>The UTF-8 bytes of the message, exactly as they are posted.</returns>
    public static byte[] Serialize(RenderedMessage message) => throw new NotImplementedException();

    #endregion
}
