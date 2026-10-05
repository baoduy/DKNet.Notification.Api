using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     A rendered notification in the delivery queue (spec §5). Fields are only ever added, as optional: a message queued
///     by one release must still be read by the next. The recipient and the body are personal data and never logged.
/// </summary>
/// <param name="SchemaVersion">The shape of this message: <see cref="CurrentSchemaVersion" />.</param>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="TemplateId">The template id the caller named.</param>
/// <param name="Channel">The channel in lower case.</param>
/// <param name="CallerId">The calling application's id.</param>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the accepting call.</param>
/// <param name="AcceptedAt">When the call was accepted.</param>
/// <param name="TraceId">The accepting call's trace id, which every delivery log entry names.</param>
/// <param name="EmailAddress">The one address an email goes to; <see langword="null" /> for Teams.</param>
/// <param name="TeamsDestination">The Teams destination name, never its webhook URL; <see langword="null" /> for email.</param>
/// <param name="Subject">The filled subject (email) or title (Teams).</param>
/// <param name="Body">The filled body.</param>
/// <param name="Format">The body format.</param>
/// <param name="AttemptsMade">Delivery attempts already made.</param>
/// <param name="NotBefore">The next attempt starts no sooner than this.</param>
public sealed record DeliverNotification(
    int SchemaVersion,
    Guid NotificationId,
    string TemplateId,
    string Channel,
    string CallerId,
    string? IdempotencyKey,
    DateTimeOffset AcceptedAt,
    string TraceId,
    string? EmailAddress,
    string? TeamsDestination,
    string Subject,
    string Body,
    BodyFormat Format,
    int AttemptsMade,
    DateTimeOffset NotBefore)
{
    #region Fields

    /// <summary>The shape this release writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The delivery queue: a Redis list, or a memory topic in local runs and tests.</summary>
    public const string QueueName = "notification-delivery";

    #endregion
}
