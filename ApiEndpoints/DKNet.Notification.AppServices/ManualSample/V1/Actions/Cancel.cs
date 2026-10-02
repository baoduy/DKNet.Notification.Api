using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.AppServices.ManualSample.V1.Actions;

/// <summary>
/// Command that cancels an existing purchase order.
/// </summary>
public sealed record CancelPurchaseOrderRequest : Fluents.Requests.IWitResponse<PurchaseOrderDto>
{
    #region Properties

    [FromClaim(ClaimTypes.Name)]
    public string? ByUser { get; set; }

    public required Guid Id { get; init; }

    #endregion
}

internal sealed class CancelPurchaseOrderCommandHandler(IRepositorySpec repository, IMapper mapper)
    : Fluents.Requests.IHandler<CancelPurchaseOrderRequest, PurchaseOrderDto>
{
    #region Methods

    public async Task<IResult<PurchaseOrderDto>> OnHandle(
        CancelPurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.ByUser))
        {
            return Result.Fail<PurchaseOrderDto>("The caller is not authenticated.");
        }

        var order = await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(request.Id), cancellationToken);

        if (order is null)
        {
            return Result.Fail<PurchaseOrderDto>(new NotFoundError($"The purchase order {request.Id} was not found."));
        }

        if (order.Status == PurchaseOrderStatus.Cancelled)
        {
            return Result.Fail<PurchaseOrderDto>(
                new Error($"The purchase order {request.Id} is already cancelled.")
                    .WithMetadata("Code", PreconditionCodes.PurchaseOrderAlreadyCancelled));
        }

        order.Cancel(request.ByUser);

        return Result.Ok(mapper.Map<PurchaseOrderDto>(order));
    }

    #endregion
}
