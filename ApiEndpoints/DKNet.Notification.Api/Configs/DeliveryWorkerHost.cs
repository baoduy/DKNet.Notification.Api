using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.Api.Configs;

/// <summary>Runs the replica's one <see cref="DeliveryWorker" /> for the life of the host.</summary>
[ExcludeFromCodeCoverage]
internal sealed class DeliveryWorkerHost(DeliveryWorker worker) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => worker.RunAsync(stoppingToken);
}
