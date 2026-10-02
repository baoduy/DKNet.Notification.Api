using System.Diagnostics;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The replica's one delivery worker (DRK-2020 §3 Delivery): it takes the notifications in line, one attempt at a
///     time. A transient failure with attempts left waits outside the line, so the next notification goes on; when
///     the wait ends it goes back in line. A stop loses every notification that has not ended, with no entry.
/// </summary>
public sealed class DeliveryWorker(
    DeliveryQueue queue,
    IDeliverySender sender,
    DeliverySettings settings,
    NotificationMetrics metrics,
    ILogger<DeliveryWorker> logger)
{
    #region Fields

    /// <summary>The activity source of the delivery attempts; the OpenTelemetry set-up exports it.</summary>
    public const string ActivitySourceName = "DKNet.Notification";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    #endregion

    #region Methods

    /// <summary>Delivers the notifications in line until <paramref name="stoppingToken" /> is cancelled.</summary>
    /// <param name="stoppingToken">Cancelled when the host stops.</param>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        // Each attempt's activity starts a trace of its own and links to its call's trace instead.
        Activity.Current = null;
        try
        {
            await foreach (var queued in queue.ReadAllAsync(stoppingToken))
            {
                await AttemptAsync(queued, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host stops: the notifications that have not ended are lost.
        }
    }

    private async Task AttemptAsync(QueuedNotification queued, CancellationToken stoppingToken)
    {
        var notification = queued.Notification;
        notification.StartAttempt();

        DeliveryFailure? failure;
        using (StartActivity(queued))
        {
            failure = await sender.SendAsync(notification, stoppingToken);
        }

        if (failure is null)
        {
            Deliver(queued);
            return;
        }

        logger.NotificationAttemptFailed(
            notification.NotificationId,
            notification.AttemptCount,
            failure.Kind,
            failure.ReplyCode,
            notification.TemplateId.SanitizeForLogging(),
            notification.Channel,
            notification.CallerId.SanitizeForLogging(),
            queued.TraceId);
        if (failure.IsTransient && notification.AttemptCount < settings.MaxAttempts)
        {
            notification.WaitForRetry();
            // Counted from the end of this attempt: now.
            _ = RequeueAfterAsync(queued, TimeSpan.FromSeconds(settings.RetryDelaysSeconds[notification.AttemptCount - 1]), stoppingToken);
            return;
        }

        Fail(queued, failure.ReplyCode);
    }

    private void Deliver(QueuedNotification queued)
    {
        var notification = queued.Notification;
        notification.Deliver();
        var duration = DateTimeOffset.UtcNow - notification.AcceptedAt;
        logger.NotificationDelivered(
            notification.NotificationId,
            notification.AttemptCount,
            duration,
            notification.TemplateId.SanitizeForLogging(),
            notification.Channel,
            notification.CallerId.SanitizeForLogging(),
            queued.TraceId);
        metrics.Delivered(notification.Channel, duration);
        queue.End();
    }

    private void Fail(QueuedNotification queued, string replyCode)
    {
        var notification = queued.Notification;
        notification.Fail();
        logger.NotificationFailed(
            notification.NotificationId,
            notification.AttemptCount,
            replyCode,
            notification.TemplateId.SanitizeForLogging(),
            notification.Channel,
            notification.CallerId.SanitizeForLogging(),
            queued.TraceId);
        metrics.Failed(notification.Channel);
        queue.End();
    }

    private async Task RequeueAfterAsync(QueuedNotification queued, TimeSpan wait, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(wait, stoppingToken);
            queue.Requeue(queued);
        }
        catch (OperationCanceledException)
        {
            // The host stops during the wait: the notification is lost.
        }
    }

    /// <summary>The attempt's activity: no recipient, value or reply text, only the notification id and attempt.</summary>
    private static Activity? StartActivity(QueuedNotification queued)
    {
        ActivityLink[]? links = ActivityContext.TryParse(queued.TraceId, traceState: null, out var accepted)
            ? [new ActivityLink(accepted)]
            : null;
        var activity = Source.StartActivity("DeliverNotification", ActivityKind.Client, parentContext: default, links: links);
        activity?.SetTag("notification.id", queued.Notification.NotificationId);
        activity?.SetTag("notification.attempt", queued.Notification.AttemptCount);
        return activity;
    }

    #endregion
}
