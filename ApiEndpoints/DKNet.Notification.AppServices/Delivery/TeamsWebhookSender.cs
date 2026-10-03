namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     Makes one delivery attempt of a <c>teams</c> notification: 1 HTTPS POST of its card to the destination's
///     webhook, within the Teams time limit, never following a redirect.
/// </summary>
public sealed class TeamsWebhookSender : IDeliverySender, IDisposable
{
    #region Fields

    /// <summary>The key the sender is registered under: the channel it delivers.</summary>
    public const string ChannelKey = "teams";

    #endregion

    #region Constructors

    /// <summary>Creates the sender of the Teams destinations in <paramref name="settings" />.</summary>
    /// <param name="settings">The Teams settings, read once at start-up.</param>
    /// <param name="trustedRoots">Certificate authorities trusted on top of the machine's own; empty in the release.</param>
    public TeamsWebhookSender(TeamsChannelSettings settings, TeamsTrustedRoots trustedRoots)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trustedRoots);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public Task<DeliveryFailure?> SendAsync(Domains.Notifications.Notification notification, CancellationToken stoppingToken) =>
        throw new NotImplementedException();

    /// <inheritdoc />
    public void Dispose()
    {
    }

    #endregion
}
