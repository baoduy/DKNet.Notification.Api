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
    private readonly Channel<QueuedNotification> _waiting =
        Channel.CreateUnbounded<QueuedNotification>(new UnboundedChannelOptions { SingleReader = true });

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
    /// <param name="traceId">The accepting call's trace id, as its log entries carry it.</param>
    /// <returns><see langword="false" /> when the queue is full: nothing is queued.</returns>
    public bool TryEnqueue(
        Domains.Notifications.Notification notification,
        EmailRecipient recipient,
        RenderedMessage renderedMessage,
        string traceId)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!TryTakePlace())
        {
            return false;
        }

        notification.Queue(recipient, renderedMessage);
        Write(notification, traceId);
        return true;
    }

    /// <summary>Queues a Teams <paramref name="notification" /> when the queue has room; email and Teams share the places.</summary>
    /// <param name="notification">A received notification, rendered and with a destination that is set.</param>
    /// <param name="recipient">The Teams destination the card goes to.</param>
    /// <param name="renderedMessage">The filled title and Markdown body.</param>
    /// <param name="traceId">The accepting call's trace id, as its log entries carry it.</param>
    /// <returns><see langword="false" /> when the queue is full: nothing is queued.</returns>
    public bool TryEnqueue(
        Domains.Notifications.Notification notification,
        TeamsRecipient recipient,
        RenderedMessage renderedMessage,
        string traceId)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!TryTakePlace())
        {
            return false;
        }

        notification.Queue(recipient, renderedMessage);
        Write(notification, traceId);
        return true;
    }

    /// <summary>Takes the notifications in line, in their order, until <paramref name="stoppingToken" /> is cancelled.</summary>
    /// <param name="stoppingToken">Cancelled when the host stops.</param>
    /// <returns>The notifications whose turn it is: queued, or back from a wait.</returns>
    public IAsyncEnumerable<QueuedNotification> ReadAllAsync(CancellationToken stoppingToken) =>
        _waiting.Reader.ReadAllAsync(stoppingToken);

    /// <summary>Puts a notification whose wait has ended back in line. It kept its place in the count.</summary>
    /// <param name="queued">A notification that waits for its next attempt.</param>
    public void Requeue(QueuedNotification queued) => _waiting.Writer.TryWrite(queued);

    /// <summary>Frees the place of a notification that ended Delivered or Failed.</summary>
    public void End() => Interlocked.Decrement(ref _length);

    // Email and Teams take their place the same way, so they share the one capacity.
    private bool TryTakePlace()
    {
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

        return true;
    }

    // An unbounded channel never completed takes every write.
    private void Write(Domains.Notifications.Notification notification, string traceId) =>
        _waiting.Writer.TryWrite(new QueuedNotification(notification, traceId));

    #endregion
}
