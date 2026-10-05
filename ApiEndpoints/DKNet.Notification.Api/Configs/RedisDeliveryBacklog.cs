using DKNet.Notification.AppServices.Delivery;
using StackExchange.Redis;

namespace DKNet.Notification.Api.Configs;

/// <summary>Counts the Redis delivery list (spec D7): <c>LLEN notification-delivery</c>.</summary>
[ExcludeFromCodeCoverage]
internal sealed class RedisDeliveryBacklog(IConnectionMultiplexer redis) : IDeliveryBacklog
{
    public async ValueTask<long> LengthAsync(CancellationToken cancellationToken) =>
        await redis.GetDatabase().ListLengthAsync(DeliverNotification.QueueName);
}
