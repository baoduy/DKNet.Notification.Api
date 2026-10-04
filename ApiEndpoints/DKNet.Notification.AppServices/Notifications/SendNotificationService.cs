using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     Steps 4 to 9 of a send call, and the log entry and count of every outcome. Caller text reaches a log entry
///     only through <see cref="SanitizeForLoggingExtensions.SanitizeForLogging" />; a parameter value, the recipient
///     and the rendered message never do.
/// </summary>
public sealed class SendNotificationService(
    ITemplateCatalogue catalogue,
    EmailChannelSettings email,
    TeamsChannelSettings teams,
    DeliveryQueue queue,
    NotificationMetrics metrics,
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

    #endregion

    #region Methods

    /// <summary>
    ///     Checks the template, the channel, the recipient and every token, then queues an email or a Teams call.
    ///     Each step runs only when the one before it passed.
    /// </summary>
    /// <param name="request">A body that passed <see cref="SendNotificationValidator" />.</param>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="traceId">The request's trace id, as its error body carries it.</param>
    /// <returns>
    ///     The notification: <see cref="NotificationStatus.Rejected" /> with its error, <see cref="NotificationStatus.Skipped" />
    ///     or <see cref="NotificationStatus.Queued" />.
    /// </returns>
    public Domains.Notifications.Notification Send(SendNotificationRequest request, string callerId, string traceId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notification = Domains.Notifications.Notification.Receive(
            request.TemplateId,
            request.Channel,
            request.Parameters,
            callerId);

        // Step 4 — template.
        var template = catalogue.Find(notification.TemplateId);
        if (template is null)
        {
            return Reject(notification, NotificationErrorCodes.TemplateNotFound, "templateId", traceId);
        }

        // Step 5 — channel.
        return notification.Channel switch
        {
            EmailChannel => SendEmail(notification, template, traceId),
            TeamsWebhookSender.ChannelKey => SendTeams(notification, template, traceId),
            _ => Skip(notification, SkipReason.ChannelNotSupported, traceId)
        };
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
    private Domains.Notifications.Notification SendEmail(
        Domains.Notifications.Notification notification,
        NotificationTemplate template,
        string traceId)
    {
        // Step 5, for email — the sender is set up, then the template has an email version.
        if (!_emailConfigured)
        {
            return Skip(notification, SkipReason.ChannelNotConfigured, traceId);
        }

        var version = template.Versions.FirstOrDefault(v => string.Equals(v.Channel, EmailChannel, StringComparison.Ordinal));
        if (version is null)
        {
            return Skip(notification, SkipReason.NoTemplateVersion, traceId);
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
        return Queued(notification, queue.TryEnqueue(notification, recipient, rendering.Message, traceId), traceId);
    }

    /// <summary>Steps 5 to 9 of a <c>teams</c> call to a registered template.</summary>
    private Domains.Notifications.Notification SendTeams(
        Domains.Notifications.Notification notification,
        NotificationTemplate template,
        string traceId)
    {
        // Step 5, for Teams — Teams is set up, then the template has a Teams version.
        if (!_teamsConfigured)
        {
            return Skip(notification, SkipReason.ChannelNotConfigured, traceId);
        }

        var version = template.Versions.FirstOrDefault(v =>
            string.Equals(v.Channel, TeamsWebhookSender.ChannelKey, StringComparison.Ordinal));
        if (version is null)
        {
            return Skip(notification, SkipReason.NoTemplateVersion, traceId);
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
            return Skip(notification, SkipReason.TeamsDestinationNotConfigured, traceId);
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

        // Step 9 — queue: the same places as email.
        return Queued(notification, queue.TryEnqueue(notification, recipient, rendering.Message, traceId), traceId);
    }

    /// <summary>Logs and counts a call the queue took, or refuses it with <see cref="NotificationErrorCodes.QueueFull" />.</summary>
    private Domains.Notifications.Notification Queued(
        Domains.Notifications.Notification notification,
        bool queued,
        string traceId)
    {
        if (!queued)
        {
            return Reject(notification, NotificationErrorCodes.QueueFull, string.Empty, traceId);
        }

        logger.NotificationQueued(
            notification.NotificationId,
            queue.Length,
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

    private Domains.Notifications.Notification Skip(
        Domains.Notifications.Notification notification,
        SkipReason reason,
        string traceId)
    {
        notification.Skip(reason);
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
