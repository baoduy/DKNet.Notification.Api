using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The replica's delivery queue. It counts a notification from the moment it is queued until it ends, and holds
///     at most <see cref="DeliverySettings.QueueCapacity" /> of them.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The design's name (docs/architect/02-domain.md \"delivery queue\"); it is not a Queue<T>.")]
public sealed class DeliveryQueue
{
    #region Fields

    private readonly int _capacity;

    // Unbounded on purpose: the capacity counts notifications that have not ended, not only those waiting here.
    private readonly Channel<Domains.Notifications.Notification> _waiting =
        Channel.CreateUnbounded<Domains.Notifications.Notification>(new UnboundedChannelOptions { SingleReader = true });

    private int _length;

    #endregion

    #region Constructors

    public DeliveryQueue(DeliverySettings settings, NotificationMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(metrics);
        _capacity = settings.QueueCapacity;
        metrics.ObserveQueueLength(() => Length);
    }

    #endregion

    #region Properties

    /// <summary>Gets how many notifications in the replica have not ended.</summary>
    public int Length => Volatile.Read(ref _length);

    #endregion

    #region Methods

    /// <summary>Queues <paramref name="notification" /> when the queue has room.</summary>
    /// <param name="notification">A received notification, rendered and with a valid recipient.</param>
    /// <param name="recipient">The one address the email goes to.</param>
    /// <param name="renderedMessage">The filled subject and body.</param>
    /// <returns><see langword="false" /> when the queue is full: nothing is queued.</returns>
    public bool TryEnqueue(
        Domains.Notifications.Notification notification,
        EmailRecipient recipient,
        RenderedMessage renderedMessage)
    {
        ArgumentNullException.ThrowIfNull(notification);

        int length;
        do
        {
            length = Length;
            if (length >= _capacity)
            {
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _length, length + 1, length) != length);

        notification.Queue(recipient, renderedMessage);
        // ponytail: nothing reads the line and nothing ends a notification until the delivery worker (DRK-2020
        // surface B) does; until then the length only grows. An unbounded channel never completed takes every write.
        _waiting.Writer.TryWrite(notification);
        return true;
    }

    #endregion
}
