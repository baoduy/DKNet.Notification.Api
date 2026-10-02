using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     Steps 4 and 5 of a send call, and the log entry and count of every refused call. Caller text reaches a
///     log entry only through <see cref="SanitizeForLoggingExtensions.SanitizeForLogging" />.
/// </summary>
public sealed class SendNotificationService(
    ITemplateCatalogue catalogue,
    NotificationMetrics metrics,
    ILogger<SendNotificationService> logger)
{
    #region Methods

    /// <summary>Checks the template, then skips the call: no channel has a sender in this release.</summary>
    /// <param name="request">A body that passed <see cref="SendNotificationValidator" />.</param>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="traceId">The request's trace id, as its error body carries it.</param>
    /// <returns>The notification, <see cref="NotificationStatus.Rejected" /> or <see cref="NotificationStatus.Skipped" />.</returns>
    public Domains.Notifications.Notification Send(SendNotificationRequest request, string callerId, string traceId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notification = Domains.Notifications.Notification.Receive(
            request.TemplateId,
            request.Channel,
            request.Parameters,
            callerId);

        if (catalogue.Find(notification.TemplateId) is null)
        {
            notification.Reject();
            logger.NotificationRejected(
                notification.NotificationId,
                NotificationErrorCodes.TemplateNotFound,
                callerId.SanitizeForLogging(),
                traceId,
                notification.TemplateId.SanitizeForLogging(),
                notification.Channel.SanitizeForLogging());
            metrics.Rejected(NotificationErrorCodes.TemplateNotFound);
            return notification;
        }

        notification.Skip(SkipReason.ChannelNotSupported);
        logger.NotificationSkipped(
            notification.NotificationId,
            SkipReason.ChannelNotSupported,
            notification.TemplateId.SanitizeForLogging(),
            notification.Channel.SanitizeForLogging(),
            callerId.SanitizeForLogging(),
            traceId);
        metrics.Accepted(notification.Channel, "skipped");
        return notification;
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

    #endregion
}
