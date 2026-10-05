using System.Diagnostics.CodeAnalysis;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     Steps 4 to 9 of a send call, and the log entry and count of every outcome; step 9 writes the pending status,
///     then puts the notification in the delivery queue. Caller text reaches a log entry only through
///     <see cref="SanitizeForLoggingExtensions.SanitizeForLogging" />; a parameter value, the recipient
///     and the rendered message never do.
/// </summary>
public sealed class SendNotificationService(
    ITemplateCatalogue catalogue,
    EmailChannelSettings email,
    TeamsChannelSettings teams,
    DeliverySettings delivery,
    IDeliveryBacklog backlog,
    IMessageBus bus,
    NotificationStatusStore status,
    NotificationMetrics metrics,
    TimeProvider time,
    ILogger<SendNotificationService> logger)
{
    #region Fields

    /// <summary>The parameter that names the Teams destination of a <c>teams</c> call.</summary>
    public const string TeamsDestinationParameter = "teamsDestination";

    private const string EmailChannel = "email";
    private const string RecipientParameter = "to";

    // The settings are read once at start-up, so whether a channel can send is decided once too.
    private readonly bool _emailConfigured = email.Enabled && email.BadSettings().Count == 0;
    private readonly bool _teamsConfigured = teams.IsConfigured;

    // The service-wide count of the shared queue, read at each scrape.
    // ponytail: a blocking LLEN per scrape; cache the last count if scrapes become frequent.
    [SuppressMessage(
        "Performance",
        "CA1823:Avoid unused private fields",
        Justification = "The initializer is the point: a primary constructor has no body to register the gauge in.")]
    private readonly bool _queueLengthObserved = ObserveQueueLength(metrics, backlog);

    #endregion

    #region Methods

    /// <summary>
    ///     Checks the template, the channel, the recipient and every token, then queues an email or a Teams call.
    ///     Each step runs only when the one before it passed.
    /// </summary>
    /// <param name="request">A body that passed <see cref="SendNotificationValidator" />.</param>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="traceId">The request's trace id, as its error body carries it.</param>
    /// <param name="idempotencyKey">The call's <c>Idempotency-Key</c>, kept in its status record; <see langword="null" /> when it has none.</param>
    /// <param name="cancellationToken">Cancels the status write and the publish.</param>
    /// <returns>
    ///     The notification: <see cref="NotificationStatus.Rejected" /> with its error, <see cref="NotificationStatus.Skipped" />
    ///     or <see cref="NotificationStatus.Queued" />.
    /// </returns>
    public async Task<Domains.Notifications.Notification> SendAsync(
        SendNotificationRequest request,
        string callerId,
        string traceId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notification = Domains.Notifications.Notification.Receive(
            request.TemplateId,
            request.Channel,
            request.Parameters,
            callerId,
            time.GetUtcNow());

        // Step 4 — template.
        var template = catalogue.Find(notification.TemplateId);
        if (template is null)
        {
            return Reject(notification, NotificationErrorCodes.TemplateNotFound, "templateId", traceId);
        }

        // Step 5 — channel.
        return notification.Channel switch
        {
            EmailChannel => await SendEmailAsync(notification, template, traceId, idempotencyKey, cancellationToken),
            TeamsWebhookSender.ChannelKey => await SendTeamsAsync(notification, template, traceId, idempotencyKey, cancellationToken),
            _ => await SkipAsync(notification, SkipReason.ChannelNotSupported, traceId, idempotencyKey, cancellationToken)
        };
    }

    private static bool ObserveQueueLength(NotificationMetrics metrics, IDeliveryBacklog backlog)
    {
        metrics.ObserveQueueLength(() => (int)backlog.LengthAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult());
        return true;
    }

    /// <summary>Logs and counts a call refused with <see cref="NotificationErrorCodes.InvalidRequest" />.</summary>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="traceId">The request's trace id, as its error body carries it.</param>
    public void RejectInvalid(string callerId, string traceId)
    {
        // The call never becomes a notification, so its id is new here and is never returned to the caller.
        logger.NotificationRejected(
            Guid.CreateVersion7(),
            NotificationErrorCodes.InvalidRequest,
            callerId.SanitizeForLogging(),
            traceId,
            templateId: null,
            channel: null);
        metrics.Rejected(NotificationErrorCodes.InvalidRequest);
    }

    /// <summary>Steps 5 to 9 of an <c>email</c> call to a registered template.</summary>
    private async Task<Domains.Notifications.Notification> SendEmailAsync(
        Domains.Notifications.Notification notification,
        NotificationTemplate template,
        string traceId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Step 5, for email — the sender is set up, then the template has an email version.
        if (!_emailConfigured)
        {
            return await SkipAsync(notification, SkipReason.ChannelNotConfigured, traceId, idempotencyKey, cancellationToken);
        }

        var version = template.Versions.FirstOrDefault(v => string.Equals(v.Channel, EmailChannel, StringComparison.Ordinal));
        if (version is null)
        {
            return await SkipAsync(notification, SkipReason.NoTemplateVersion, traceId, idempotencyKey, cancellationToken);
        }

        // Step 6 — recipient, never trimmed.
        if (!notification.Parameters.TryGetValue(RecipientParameter, out var to) || to.Length == 0)
        {
            return Reject(notification, NotificationErrorCodes.RecipientMissing, RecipientParameter, traceId);
        }

        if (!EmailRecipient.TryCreate(to, out var recipient))
        {
            return Reject(notification, NotificationErrorCodes.RecipientInvalid, RecipientParameter, traceId);
        }

        // Step 8 — rendering, exactly once, before the call is queued.
        var rendering = EmailRenderer.Render(version, notification.Parameters);
        if (rendering.Message is null)
        {
            return Reject(
                notification,
                NotificationErrorCodes.ParameterMissing,
                $"parameters.{rendering.MissingParameter}",
                traceId);
        }

        // Step 9 — queue.
        return await QueueAsync(notification, recipient, teams: null, rendering.Message, traceId, idempotencyKey, cancellationToken);
    }

    /// <summary>Steps 5 to 9 of a <c>teams</c> call to a registered template.</summary>
    private async Task<Domains.Notifications.Notification> SendTeamsAsync(
        Domains.Notifications.Notification notification,
        NotificationTemplate template,
        string traceId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Step 5, for Teams — Teams is set up, then the template has a Teams version.
        if (!_teamsConfigured)
        {
            return await SkipAsync(notification, SkipReason.ChannelNotConfigured, traceId, idempotencyKey, cancellationToken);
        }

        var version = template.Versions.FirstOrDefault(v =>
            string.Equals(v.Channel, TeamsWebhookSender.ChannelKey, StringComparison.Ordinal));
        if (version is null)
        {
            return await SkipAsync(notification, SkipReason.NoTemplateVersion, traceId, idempotencyKey, cancellationToken);
        }

        // Step 6 — destination, never trimmed or lower-cased, then set in this deployment. Never logged.
        if (!notification.Parameters.TryGetValue(TeamsDestinationParameter, out var destination) || destination.Length == 0)
        {
            return Reject(notification, NotificationErrorCodes.RecipientMissing, TeamsDestinationParameter, traceId);
        }

        if (!TeamsRecipient.TryCreate(destination, out var recipient))
        {
            return Reject(notification, NotificationErrorCodes.RecipientInvalid, TeamsDestinationParameter, traceId);
        }

        if (teams.WebhookFor(recipient.Name) is null)
        {
            return await SkipAsync(notification, SkipReason.TeamsDestinationNotConfigured, traceId, idempotencyKey, cancellationToken);
        }

        // Step 8 — rendering, exactly once, before the call is queued; the size is that of the bytes posted.
        var rendering = TeamsRenderer.Render(version, notification.Parameters);
        if (rendering.Message is null)
        {
            return Reject(
                notification,
                NotificationErrorCodes.ParameterMissing,
                $"parameters.{rendering.MissingParameter}",
                traceId);
        }

        if (TeamsCard.Serialize(rendering.Message).Length > TeamsCard.MaxBytes)
        {
            return Reject(notification, NotificationErrorCodes.MessageTooLarge, string.Empty, traceId);
        }

        // Step 9 — queue: the same queue as email.
        return await QueueAsync(notification, email: null, recipient, rendering.Message, traceId, idempotencyKey, cancellationToken);
    }

    /// <summary>
    ///     Refuses the call with <see cref="NotificationErrorCodes.QueueFull" /> when the queue holds
    ///     <see cref="DeliverySettings.QueueCapacity" /> notifications; otherwise writes its pending status, then publishes it.
    ///     A publish that throws is not caught: the call answers 500.
    /// </summary>
    private async Task<Domains.Notifications.Notification> QueueAsync(
        Domains.Notifications.Notification notification,
        EmailRecipient? email,
        TeamsRecipient? teams,
        RenderedMessage rendered,
        string traceId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var length = await backlog.LengthAsync(cancellationToken);
        if (length >= delivery.QueueCapacity)
        {
            return Reject(notification, NotificationErrorCodes.QueueFull, string.Empty, traceId);
        }

        if (email is not null)
        {
            notification.Queue(email, rendered);
        }
        else
        {
            notification.Queue(teams!, rendered);
        }

        // Before the publish: the consumer may finish before this call returns, and its final status must win.
        await status.WriteAsync(
            notification.CallerId,
            new NotificationStatusRecord(notification.NotificationId, idempotencyKey, NotificationOutcome.Pending),
            cancellationToken);
        await bus.Publish(
            new DeliverNotification(
                DeliverNotification.CurrentSchemaVersion,
                notification.NotificationId,
                notification.TemplateId,
                notification.Channel,
                notification.CallerId,
                idempotencyKey,
                notification.AcceptedAt,
                traceId,
                email?.Address,
                teams?.Name,
                rendered.Subject,
                rendered.Body,
                rendered.Format,
                AttemptsMade: 0,
                NotBefore: time.GetUtcNow()),
            cancellationToken: cancellationToken);

        logger.NotificationQueued(
            notification.NotificationId,
            (int)(length + 1),
            notification.TemplateId.SanitizeForLogging(),
            notification.Channel,
            notification.CallerId.SanitizeForLogging(),
            traceId);
        metrics.Accepted(notification.Channel, "queued");
        return notification;
    }

    private Domains.Notifications.Notification Reject(
        Domains.Notifications.Notification notification,
        string code,
        string field,
        string traceId)
    {
        notification.Reject(code, field);
        logger.NotificationRejected(
            notification.NotificationId,
            code,
            notification.CallerId.SanitizeForLogging(),
            traceId,
            notification.TemplateId.SanitizeForLogging(),
            notification.Channel.SanitizeForLogging());
        metrics.Rejected(code);
        return notification;
    }

    // A skipped call is final at once, so its status is failed (spec §6).
    private async Task<Domains.Notifications.Notification> SkipAsync(
        Domains.Notifications.Notification notification,
        SkipReason reason,
        string traceId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        notification.Skip(reason);
        await status.WriteAsync(
            notification.CallerId,
            new NotificationStatusRecord(notification.NotificationId, idempotencyKey, NotificationOutcome.Failed),
            cancellationToken);
        logger.NotificationSkipped(
            notification.NotificationId,
            reason,
            notification.TemplateId.SanitizeForLogging(),
            notification.Channel.SanitizeForLogging(),
            notification.CallerId.SanitizeForLogging(),
            traceId);
        metrics.Accepted(notification.Channel, "skipped");
        return notification;
    }

    #endregion
}
