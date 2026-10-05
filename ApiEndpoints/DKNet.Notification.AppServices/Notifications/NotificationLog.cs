using DKNet.Notification.Domains.Notifications;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     The notification log entries of DRK-2013 "Logs and metrics" and DRK-2020 "Logs". No entry takes a parameter
///     value, a recipient, the rendered message or a provider's error text.
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

    /// <remarks><paramref name="replyCode" /> is the provider status code only, empty when there was no reply.</remarks>
    [LoggerMessage(
        EventId = 2004,
        EventName = "NotificationAttemptFailed",
        Level = LogLevel.Warning,
        Message = "Notification {NotificationId} attempt {Attempt} failed: {FailureKind}, provider status {ReplyCode}. Template {TemplateId}, channel {Channel}, caller {CallerId}, trace {TraceId}.")]
    public static partial void NotificationAttemptFailed(
        this ILogger logger,
        Guid notificationId,
        int attempt,
        string failureKind,
        string replyCode,
        string templateId,
        string channel,
        string callerId,
        string traceId);

    [LoggerMessage(
        EventId = 2005,
        EventName = "NotificationDelivered",
        Level = LogLevel.Information,
        Message = "Notification {NotificationId} delivered on attempt {Attempt}, {Duration} after it was accepted. Template {TemplateId}, channel {Channel}, caller {CallerId}, trace {TraceId}.")]
    public static partial void NotificationDelivered(
        this ILogger logger,
        Guid notificationId,
        int attempt,
        TimeSpan duration,
        string templateId,
        string channel,
        string callerId,
        string traceId);

    /// <remarks><paramref name="replyCode" /> is the last provider status code only, empty when there was no reply.</remarks>
    [LoggerMessage(
        EventId = 2006,
        EventName = "NotificationFailed",
        Level = LogLevel.Error,
        Message = "Notification {NotificationId} failed after {AttemptCount} attempts, last provider status {ReplyCode}. Template {TemplateId}, channel {Channel}, caller {CallerId}, trace {TraceId}.")]
    public static partial void NotificationFailed(
        this ILogger logger,
        Guid notificationId,
        int attemptCount,
        string replyCode,
        string templateId,
        string channel,
        string callerId,
        string traceId);

    /// <remarks>Ids and the status only: the error is not logged, as its text may hold a connection string.</remarks>
    [LoggerMessage(
        EventId = 2007,
        EventName = "NotificationStatusWriteFailed",
        Level = LogLevel.Warning,
        Message = "The status {Status} of notification {NotificationId} could not be written. Caller {CallerId}.")]
    public static partial void NotificationStatusWriteFailed(
        this ILogger logger,
        Guid notificationId,
        NotificationOutcome status,
        string callerId);

    #endregion
}
