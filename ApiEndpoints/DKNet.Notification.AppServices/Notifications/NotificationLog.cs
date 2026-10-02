using DKNet.Notification.Domains.Notifications;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     The notification log entries of DRK-2013 "Logs and metrics". No entry takes a parameter value or a
///     recipient.
/// </summary>
internal static partial class NotificationLog
{
    #region Methods

    [LoggerMessage(
        EventId = 2001,
        EventName = "NotificationSkipped",
        Level = LogLevel.Warning,
        Message = "Notification {NotificationId} skipped: {Reason}. Template {TemplateId}, channel {Channel}, caller {CallerId}, trace {TraceId}.")]
    public static partial void NotificationSkipped(
        this ILogger logger,
        Guid notificationId,
        SkipReason reason,
        string templateId,
        string channel,
        string callerId,
        string traceId);

    /// <remarks>
    ///     <paramref name="templateId" /> and <paramref name="channel" /> are given for every code but
    ///     <see cref="NotificationErrorCodes.InvalidRequest" />: an <see cref="NotificationErrorCodes.InvalidRequest" />
    ///     body has no field the entry may trust.
    /// </remarks>
    [LoggerMessage(
        EventId = 2002,
        EventName = "NotificationRejected",
        Level = LogLevel.Information,
        Message = "Notification {NotificationId} rejected: {Code}. Caller {CallerId}, trace {TraceId}, template {TemplateId}, channel {Channel}.")]
    public static partial void NotificationRejected(
        this ILogger logger,
        Guid notificationId,
        string code,
        string callerId,
        string traceId,
        string? templateId,
        string? channel);

    [LoggerMessage(
        EventId = 2003,
        EventName = "NotificationQueued",
        Level = LogLevel.Information,
        Message = "Notification {NotificationId} queued; queue length {QueueLength}. Template {TemplateId}, channel {Channel}, caller {CallerId}, trace {TraceId}.")]
    public static partial void NotificationQueued(
        this ILogger logger,
        Guid notificationId,
        int queueLength,
        string templateId,
        string channel,
        string callerId,
        string traceId);

    #endregion
}
