using Microsoft.Extensions.Logging;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.AppServices.ManualSample.V1.Events;

/// <summary>
/// Consumes <see cref="PurchaseOrderCreatedEvent" />, raised by hand from <see cref="PurchaseOrder" />'s constructor.
/// </summary>
internal sealed class PurchaseOrderCreatedEventHandler(ILogger<PurchaseOrderCreatedEventHandler> logger)
    : Fluents.EventsConsumers.IHandler<PurchaseOrderCreatedEvent>
{
    #region Methods

    public Task OnHandle(PurchaseOrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "PurchaseOrderCreatedEvent received for purchase order {PurchaseOrderId} ({CustomerName}, {Amount}).",
                notification.Id,
                notification.CustomerName,
                notification.Amount);
        }

        return Task.CompletedTask;
    }

    #endregion
}
