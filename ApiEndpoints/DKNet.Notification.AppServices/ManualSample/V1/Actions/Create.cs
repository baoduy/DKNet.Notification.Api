using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

// ReSharper disable UnusedType.Global

namespace DKNet.Notification.AppServices.ManualSample.V1.Actions;

/// <summary>
/// Command to create a new purchase order.
/// </summary>
public sealed record CreatePurchaseOrderRequest : Fluents.Requests.IWitResponse<PurchaseOrderDto>
{
    #region Properties

    /// <summary>
    /// Gets or sets the identity of the acting user. Overwritten by <c>AddContextualRequestPopulation</c> from
    /// the caller's <see cref="ClaimTypes.Name" /> claim before validation and before the handler runs — a
    /// payload value is never trusted (R1).
    /// </summary>
    [FromClaim(ClaimTypes.Name)]
    public string? ByUser { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string CustomerName { get; set; } = null!;

    public decimal Amount { get; set; }

    #endregion
}

internal sealed class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    #region Constructors

    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(a => a.CustomerName).NotEmpty().Length(1, 200);
        RuleFor(a => a.Amount).GreaterThan(0);
    }

    #endregion
}

/// <summary>
/// Handles <see cref="CreatePurchaseOrderRequest" /> by constructing the aggregate — which raises
/// <see cref="PurchaseOrderCreatedEvent" /> itself in code — and persisting it.
/// </summary>
internal sealed class CreatePurchaseOrderCommandHandler(IRepositorySpec repository, IMapper mapper)
    : Fluents.Requests.IHandler<CreatePurchaseOrderRequest, PurchaseOrderDto>
{
    #region Methods

    public async Task<IResult<PurchaseOrderDto>> OnHandle(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.ByUser))
        {
            return Result.Fail<PurchaseOrderDto>("The caller is not authenticated.");
        }

        var order = new PurchaseOrder(request.CustomerName, request.Amount, request.ByUser);

        await repository.AddAsync(order, cancellationToken);

        return mapper.ResultOf<PurchaseOrderDto>(order);
    }

    #endregion
}
