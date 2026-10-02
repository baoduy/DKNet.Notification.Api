using DKNet.SlimBus.Extensions;
using Microsoft.Extensions.Logging;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;

namespace DKNet.Notification.Infra.Features.AutomatedSample.ExternalEvents;

/// <summary>
/// External-broker subscriber proving a declaratively raised <see cref="ProductCreatedEvent"/> reaches
/// the <c>product-tp</c> Azure Service Bus topic and its subscription.
/// </summary>
internal sealed class ProductCreatedNotificationHandler(ILogger<ProductCreatedNotificationHandler> logger)
    : Fluents.EventsConsumers.IHandler<ProductCreatedEvent>
{
    #region Methods

    /// <inheritdoc />
    public Task OnHandle(ProductCreatedEvent notification, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("External broker received product-created event for {ProductId}", notification.Id);
        }

        return Task.CompletedTask;
    }

    #endregion
}
