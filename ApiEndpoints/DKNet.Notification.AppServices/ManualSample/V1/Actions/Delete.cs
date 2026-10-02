using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;

namespace DKNet.Notification.AppServices.ManualSample.V1.Actions;

/// <summary>
/// Command to delete an existing purchase order by its unique identifier.
/// </summary>
public sealed record DeletePurchaseOrderRequest : Fluents.Requests.INoResponse
{
    #region Properties

    [FromClaim(ClaimTypes.Name)]
    public string? ByUser { get; set; }

    public required Guid Id { get; init; }

    #endregion
}

internal sealed class DeletePurchaseOrderCommandHandler(IRepositorySpec repository)
    : Fluents.Requests.IHandler<DeletePurchaseOrderRequest>
{
    #region Methods

    public async Task<IResultBase> OnHandle(DeletePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.ByUser))
        {
            return Result.Fail("The caller is not authenticated.");
        }

        var order = await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(request.Id), cancellationToken);

        if (order is null)
        {
            return Result.Fail(new NotFoundError($"The purchase order {request.Id} was not found."));
        }

        repository.Delete(order);

        return Result.Ok();
    }

    #endregion
}
