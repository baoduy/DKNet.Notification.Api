using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;

namespace DKNet.Notification.AppServices.ManualSample.V1.Actions;

/// <summary>
/// Command that changes the amount of an existing purchase order.
/// </summary>
public sealed record UpdatePurchaseOrderRequest : Fluents.Requests.IWitResponse<PurchaseOrderDto>
{
    #region Properties

    [FromClaim(ClaimTypes.Name)]
    public string? ByUser { get; set; }

    public Guid Id { get; init; }

    public decimal Amount { get; init; }

    #endregion
}

internal sealed class UpdatePurchaseOrderCommandValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    #region Constructors

    public UpdatePurchaseOrderCommandValidator()
    {
        // Id is supplied from the route, not the body — an unknown/empty Id is a 404 from the
        // spec lookup below, not a validation error.
        RuleFor(a => a.Amount).GreaterThan(0);
    }

    #endregion
}

internal sealed class UpdatePurchaseOrderCommandHandler(IRepositorySpec repository, IMapper mapper)
    : Fluents.Requests.IHandler<UpdatePurchaseOrderRequest, PurchaseOrderDto>
{
    #region Methods

    public async Task<IResult<PurchaseOrderDto>> OnHandle(
        UpdatePurchaseOrderRequest request,
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

        order.ChangeAmount(request.Amount, request.ByUser);

        return Result.Ok(mapper.Map<PurchaseOrderDto>(order));
    }

    #endregion
}
