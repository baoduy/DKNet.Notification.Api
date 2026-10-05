using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     Delivers one queued notification per message (spec §6): one attempt, then a final status, or the message back in
///     the queue for its next attempt. No exception leaves it: the Redis consumer would drop the message.
/// </summary>
/// <param name="bus">The bus the message goes back to.</param>
/// <param name="sender">The email sender: every channel but <c>teams</c> goes through it.</param>
/// <param name="settings">The attempts and waits.</param>
/// <param name="status">The caller-scoped status the final outcome is written to.</param>
/// <param name="metrics">The delivery counters.</param>
/// <param name="time">The clock the waits and the delivery duration are measured on.</param>
/// <param name="logger">The delivery log entries.</param>
/// <param name="teams">
///     The Teams sender, registered under <see cref="TeamsWebhookSender.ChannelKey" />; <see langword="null" /> when
///     none is, and then a <c>teams</c> notification ends as a permanent failure with no reply.
/// </param>
internal sealed class DeliveryConsumer(
    IMessageBus bus,
    IDeliverySender sender,
    DeliverySettings settings,
    NotificationStatusStore status,
    NotificationMetrics metrics,
    TimeProvider time,
    ILogger<DeliveryConsumer> logger,
    [FromKeyedServices(TeamsWebhookSender.ChannelKey)] IDeliverySender? teams = null) : IConsumer<DeliverNotification>
{
    #region Fields

    /// <summary>The activity source of the delivery attempts; the OpenTelemetry set-up exports it.</summary>
    public const string ActivitySourceName = NotificationMetrics.DeliveryActivitySourceName;

    /// <summary>The longest a not-yet-due message holds the consumer before it takes the next one.</summary>
    public static readonly TimeSpan NotDuePause = TimeSpan.FromSeconds(1);

    private static readonly ActivitySource Source = new(ActivitySourceName);

    private static readonly DeliveryFailure UnexpectedError = new(IsTransient: false, ReplyCode: string.Empty);

    #endregion

    #region Methods

    /// <inheritdoc />
    public async Task OnHandle(DeliverNotification message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = time.GetUtcNow();
        if (message.NotBefore > now)
        {
            if (!await RequeueAsync(message, "not-due"))
            {
                await EndAsync(message, notification: null, NotificationOutcome.Failed, string.Empty);
                return;
            }

            await PauseAsync(message.NotBefore - now < NotDuePause ? message.NotBefore - now : NotDuePause, cancellationToken);
            return;
        }

        if (!TryResume(message, out var notification))
        {
            await EndAsync(message, notification: null, NotificationOutcome.Failed, string.Empty);
            return;
        }

        notification.StartAttempt();
        DeliveryFailure? failure;
        using (StartActivity(message, notification.AttemptCount))
        {
            try
            {
                var channelSender = string.Equals(notification.Channel, TeamsWebhookSender.ChannelKey, StringComparison.Ordinal)
                    ? teams
                    : sender;
                failure = channelSender is null
                    ? UnexpectedError
                    : await channelSender.SendAsync(notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The host stops: the cut-off attempt is not counted (spec §6).
                if (!await RequeueAsync(message, "stopping"))
                {
                    await EndAsync(message, notification: null, NotificationOutcome.Failed, string.Empty);
                }

                return;
            }
#pragma warning disable CA1031 // An unexpected error ends this notification only; its text may hold the recipient.
            catch (Exception)
#pragma warning restore CA1031
            {
                failure = UnexpectedError;
            }
        }

        if (failure is null)
        {
            await EndAsync(message, notification, NotificationOutcome.Success, string.Empty);
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
            message.TraceId);
        if (failure.IsTransient && notification.AttemptCount < settings.MaxAttempts)
        {
            // Counted from the end of this attempt: now. A wait the provider asked for replaces the configured one.
            var wait = failure.RetryAfter ?? TimeSpan.FromSeconds(settings.RetryDelaysSeconds[notification.AttemptCount - 1]);
            if (!await RequeueAsync(message with { AttemptsMade = notification.AttemptCount, NotBefore = time.GetUtcNow() + wait }, "retry"))
            {
                await EndAsync(message, notification, NotificationOutcome.Failed, failure.ReplyCode);
            }

            return;
        }

        await EndAsync(message, notification, NotificationOutcome.Failed, failure.ReplyCode);
    }

    // Always CancellationToken.None: a stopping host must still put the message back (SMB publishes until it is disposed).
    // False when the publish failed: the message is lost, so the caller ends the notification failed instead of leaving it pending.
    private async Task<bool> RequeueAsync(DeliverNotification message, string reason)
    {
        try
        {
            await bus.Publish(message, cancellationToken: CancellationToken.None);
        }
#pragma warning disable CA1031 // A failed publish ends this notification only; the error text may hold a connection string.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }

        logger.NotificationRequeued(message.NotificationId, message.AttemptsMade, reason, message.TraceId);
        return true;
    }

    private async Task PauseAsync(TimeSpan pause, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(pause, time, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host stops during the pause: the message is already back in the queue.
        }
    }

    private static bool TryResume(DeliverNotification message, [NotNullWhen(true)] out Domains.Notifications.Notification? notification)
    {
        notification = null;
        EmailRecipient? email = null;
        TeamsRecipient? teamsRecipient = null;
        if (message.EmailAddress is not null && !EmailRecipient.TryCreate(message.EmailAddress, out email))
        {
            return false;
        }

        if (message.TeamsDestination is not null && !TeamsRecipient.TryCreate(message.TeamsDestination, out teamsRecipient))
        {
            return false;
        }

        try
        {
            notification = Domains.Notifications.Notification.Resume(
                message.NotificationId,
                message.TemplateId,
                message.Channel,
                message.CallerId,
                message.AcceptedAt,
                email,
                teamsRecipient,
                new RenderedMessage(message.Subject, message.Body, message.Format),
                message.AttemptsMade);
            return true;
        }
        catch (ArgumentException)
        {
            // Covers ArgumentOutOfRangeException: no recipient, two recipients or attempts out of range.
            return false;
        }
    }

    private async Task EndAsync(DeliverNotification message, Domains.Notifications.Notification? notification, NotificationOutcome outcome, string replyCode)
    {
        var attempts = notification?.AttemptCount ?? message.AttemptsMade;
        if (outcome == NotificationOutcome.Success)
        {
            notification!.Deliver();
            var duration = time.GetUtcNow() - message.AcceptedAt;
            logger.NotificationDelivered(
                message.NotificationId,
                attempts,
                duration,
                message.TemplateId.SanitizeForLogging(),
                message.Channel,
                message.CallerId.SanitizeForLogging(),
                message.TraceId);
            metrics.Delivered(message.Channel, duration);
        }
        else
        {
            notification?.Fail();
            logger.NotificationFailed(
                message.NotificationId,
                attempts,
                replyCode,
                message.TemplateId.SanitizeForLogging(),
                message.Channel,
                message.CallerId.SanitizeForLogging(),
                message.TraceId);
            metrics.Failed(message.Channel);
        }

        await status.WriteAsync(
            message.CallerId,
            new NotificationStatusRecord(message.NotificationId, message.IdempotencyKey, outcome),
            CancellationToken.None);
    }

    /// <summary>The attempt's activity: no recipient, value or reply text, only the notification id and attempt.</summary>
    private static Activity? StartActivity(DeliverNotification message, int attempt)
    {
        ActivityLink[]? links = ActivityContext.TryParse(message.TraceId, traceState: null, out var accepted)
            ? [new ActivityLink(accepted)]
            : null;
        // Each attempt starts a trace of its own and links to its call's trace instead: a default parent context would
        // still take the ambient activity (the bus consumer's) as parent.
        Activity.Current = null;
        var activity = Source.StartActivity("DeliverNotification", ActivityKind.Client, parentContext: default, links: links);
        activity?.SetTag("notification.id", message.NotificationId);
        activity?.SetTag("notification.attempt", attempt);
        return activity;
    }

    #endregion
}
