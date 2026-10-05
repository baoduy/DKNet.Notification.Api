using DKNet.Notification.Api.Configs;
using DKNet.Notification.AppServices.Delivery;
using StackExchange.Redis;

namespace DKNet.Notification.App.Tests.Unit.Configs;

/// <summary>
/// The delivery bus keeps its Redis connection to itself: the idempotency store registers its own
/// <see cref="IConnectionMultiplexer" />, and a second registration would replace it (the last one wins). Registration
/// only, so no Redis server is needed.
/// </summary>
public sealed class ServiceConfigsTests
{
    [Fact]
    public void The_delivery_wiring_leaves_the_idempotency_store_its_own_Redis_connection()
    {
        var services = new ServiceCollection().AddAllAppServices("localhost:6379,abortConnect=false");

        services.ShouldNotContain(d => d.ServiceType == typeof(IConnectionMultiplexer));
        services.ShouldContain(d => d.ServiceType == typeof(IDeliveryBacklog));
    }
}
