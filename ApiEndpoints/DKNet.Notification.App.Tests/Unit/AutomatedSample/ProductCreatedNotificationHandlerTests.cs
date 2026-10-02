using Microsoft.Extensions.Logging;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;
using DKNet.Notification.Infra.Features.AutomatedSample.ExternalEvents;

namespace DKNet.Notification.App.Tests.Unit.AutomatedSample;

/// <summary>
/// The external-broker subscriber proving a declaratively raised <see cref="ProductCreatedEvent"/> reaches
/// the external topic. The BDD/xUnit test hosts never configure <c>ConnectionStrings:AzureBus</c> (the Azure
/// child bus, and this handler with it, is only wired when that connection string is non-empty — see
/// <c>ServiceBusSetup.AddAzureBus</c>), so nothing in the suite ever routes a message to this handler. Invoke
/// it directly to prove its own behavior.
/// </summary>
public class ProductCreatedNotificationHandlerTests
{
    [Fact]
    public async Task OnHandle_ShouldLogTheExternalBrokerReceipt()
    {
        var logCapture = new TestLogCapture();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logCapture));
        var handler = new ProductCreatedNotificationHandler(loggerFactory.CreateLogger<ProductCreatedNotificationHandler>());
        var productId = Guid.NewGuid();
        var notification = new ProductCreatedEvent { Id = productId, Name = "Widget", Price = 9.99m };

        await handler.OnHandle(notification, CancellationToken.None);

        logCapture.Messages.ShouldContain(m => m.Contains(productId.ToString(), StringComparison.Ordinal));
    }
}
