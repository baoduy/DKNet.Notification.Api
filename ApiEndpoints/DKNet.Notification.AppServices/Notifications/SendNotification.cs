using DKNet.Notification.Domains.Notifications;
using DKNet.SlimBus.Extensions;
using FluentResults;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     A send call whose body passed validation, sent on the in-memory bus (ADR-0011). The caller id and the trace
///     id are read from the HTTP request by the endpoint, so the handler never needs the request.
/// </summary>
/// <param name="Request">The validated body.</param>
/// <param name="CallerId">The caller that passed step 1.</param>
/// <param name="TraceId">The trace id an error body and every log entry name.</param>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> header of the call, kept in its status record.</param>
public sealed record SendNotification(SendNotificationRequest Request, string CallerId, string TraceId, string? IdempotencyKey = null)
    : Fluents.Requests.IWitResponse<Guid>
{
    #region Fields

    /// <summary>The error metadata key of the refusal's code; the key DKNet's error responses read.</summary>
    public const string CodeMetadata = "Code";

    /// <summary>The error metadata key of the refused field; empty when the refusal names no field.</summary>
    public const string FieldMetadata = "Field";

    #endregion
}

/// <summary>
///     Runs <see cref="SendNotificationService.SendAsync" />. A queued or skipped notification answers its id, so a
///     caller cannot tell them apart; a rejected one fails with its code and field as error metadata.
/// </summary>
internal sealed class SendNotificationHandler(SendNotificationService service)
    : Fluents.Requests.IHandler<SendNotification, Guid>
{
    #region Methods

    public async Task<IResult<Guid>> OnHandle(SendNotification request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notification = await service.SendAsync(request.Request, request.CallerId, request.TraceId, request.IdempotencyKey, cancellationToken);
        return notification.Status == NotificationStatus.Rejected
            ? Result.Fail<Guid>(new Error(notification.ErrorCode)
                .WithMetadata(SendNotification.CodeMetadata, notification.ErrorCode ?? string.Empty)
                .WithMetadata(SendNotification.FieldMetadata, notification.ErrorField ?? string.Empty))
            : Result.Ok(notification.NotificationId);
    }

    #endregion
}
