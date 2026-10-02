using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;

namespace DKNet.Notification.AppServices.ManualSample.V1.Queries;

public sealed record GetPurchaseOrderByIdQuery : Fluents.Queries.IWitResponse<PurchaseOrderDto>
{
    public required Guid Id { get; init; }
}

internal sealed class GetPurchaseOrderByIdQueryHandler(IRepositorySpec repository, IMapper mapper)
    : Fluents.Queries.IHandler<GetPurchaseOrderByIdQuery, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto?> OnHandle(
        GetPurchaseOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var order = await repository.FirstOrDefaultAsync(new SpecGetPurchaseOrder(request.Id), cancellationToken);

        return order is null ? null : mapper.Map<PurchaseOrderDto>(order);
    }
}
