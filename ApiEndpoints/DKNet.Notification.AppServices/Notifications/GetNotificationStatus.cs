using DKNet.SlimBus.Extensions;
using FluentResults;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>A caller asks where its notification stands (spec §8), on the in-memory bus (ADR-0011).</summary>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="CallerId">The caller that passed sign-in.</param>
public sealed record GetNotificationStatus(Guid NotificationId, string CallerId)
    : Fluents.Requests.IWitResponse<NotificationStatusRecord>;

/// <summary>Reads the caller's own status record; fails with <see cref="NotificationErrorCodes.NotificationNotFound" /> otherwise.</summary>
internal sealed class GetNotificationStatusHandler(NotificationStatusStore status)
    : Fluents.Requests.IHandler<GetNotificationStatus, NotificationStatusRecord>
{
    public async Task<IResult<NotificationStatusRecord>> OnHandle(GetNotificationStatus request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var record = await status.ReadAsync(request.CallerId, request.NotificationId, cancellationToken);
        return record is null
            ? Result.Fail<NotificationStatusRecord>(new Error(NotificationErrorCodes.NotificationNotFound)
                .WithMetadata(SendNotification.CodeMetadata, NotificationErrorCodes.NotificationNotFound)
                .WithMetadata(SendNotification.FieldMetadata, "notificationId"))
            : Result.Ok(record);
    }
}
