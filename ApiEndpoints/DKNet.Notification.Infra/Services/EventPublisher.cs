using DKNet.EfCore.Abstractions.Events;
using SlimMessageBus;

namespace DKNet.Notification.Infra.Services;

/// <summary>
///     The event publisher, IMessageBus for both internal and external events.
/// </summary>
/// <param name="bus"></param>
internal sealed class EventPublisher(IMessageBus bus) : DefaultEventPublisher
{
    #region Methods

    public override async Task PublishAsync(object eventObj, CancellationToken cancellationToken = default)
    {
        await bus.Publish(eventObj, cancellationToken: cancellationToken);
    }

    #endregion
}