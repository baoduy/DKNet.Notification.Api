using System.Text.Json;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     The status records (spec §7), over the shared <see cref="IDistributedCache" />: Redis, or memory in local runs and
///     tests. A record is keyed by its caller, so a caller only ever reads its own.
/// </summary>
public sealed class NotificationStatusStore(
    IDistributedCache cache,
    NotificationStatusSettings settings,
    ILogger<NotificationStatusStore> logger)
{
    #region Methods

    /// <summary>The cache key of a caller's notification.</summary>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="notificationId">The notification id.</param>
    /// <returns><c>status:{callerId}:{notificationId}</c>.</returns>
    public static string KeyOf(string callerId, Guid notificationId) => $"status:{callerId}:{notificationId}";

    /// <summary>
    ///     Writes a status, kept for the retention time from now. A write that fails is logged and dropped: it never
    ///     fails the call or the delivery.
    /// </summary>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="record">The status to keep.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task WriteAsync(string callerId, NotificationStatusRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callerId);
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            await cache.SetAsync(
                KeyOf(callerId, record.NotificationId),
                JsonSerializer.SerializeToUtf8Bytes(record),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(settings.RetentionHours) },
                cancellationToken);
        }
#pragma warning disable CA1031 // A status write is best effort (spec §7); the error text may hold a connection string.
        catch (Exception)
#pragma warning restore CA1031
        {
            logger.NotificationStatusWriteFailed(record.NotificationId, record.Status, callerId.SanitizeForLogging());
        }
    }

    /// <summary>Reads a caller's status; <see langword="null" /> when there is none, or it is another caller's, or it expired.</summary>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="notificationId">The notification id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" />.</returns>
    public async Task<NotificationStatusRecord?> ReadAsync(string callerId, Guid notificationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callerId);
        var bytes = await cache.GetAsync(KeyOf(callerId, notificationId), cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<NotificationStatusRecord>(bytes);
    }

    #endregion
}
