using Microsoft.Extensions.Logging;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;

namespace DKNet.Notification.AppServices.AutomatedSample.V1.Events;

/// <summary>
/// Internal (in-memory bus) subscriber for <see cref="ProductCreatedEvent"/>. Hand-written: the
/// <c>[RaisesEvent]</c> generator only declares and raises the event, it does not generate consumers.
/// </summary>
internal sealed class ProductCreatedEventHandler(ILogger<ProductCreatedEventHandler> logger)
    : Fluents.EventsConsumers.IHandler<ProductCreatedEvent>
{
    #region Methods

    /// <inheritdoc />
    public Task OnHandle(ProductCreatedEvent notification, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("AutomatedSample product created: {ProductId}", notification.Id);
        }

        return Task.CompletedTask;
    }

    #endregion
}
