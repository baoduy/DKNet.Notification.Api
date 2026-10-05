namespace DKNet.Notification.AppServices.Delivery;

/// <summary>How many notifications wait in the delivery queue, for the whole service (spec D7).</summary>
public interface IDeliveryBacklog
{
    /// <summary>Counts the notifications waiting in the queue. Approximate: replicas publish at the same time.</summary>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>The number waiting.</returns>
    ValueTask<long> LengthAsync(CancellationToken cancellationToken);
}

/// <summary>The memory delivery bus of local runs and tests has no list to count, so the queue never fills.</summary>
public sealed class InProcessDeliveryBacklog : IDeliveryBacklog
{
    /// <inheritdoc />
    public ValueTask<long> LengthAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0L);
}
