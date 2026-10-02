using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.Notification.AppServices.ManualSample.V1.Specs;
using X.PagedList;

namespace DKNet.Notification.AppServices.ManualSample.V1.Queries;

public sealed record ListPurchaseOrdersQuery : Fluents.Queries.IWitPageResponse<PurchaseOrderDto>
{
    /// <summary>
    /// Declared default page index, applied when the caller omits <c>pageIndex</c> from the query string.
    /// </summary>
    public const int DefaultPageIndex = 1;

    /// <summary>
    /// Declared default page size, applied when the caller omits <c>pageSize</c> from the query string.
    /// </summary>
    public const int DefaultPageSize = 20;

    // Nullable so [AsParameters] leaves these `null` (rather than the CLR default `0`) when the caller
    // never supplied the query parameter — distinguishing "not supplied" (use the declared default) from
    // an explicit out-of-range value like `pageSize=0` (must still 400). See DRK-738 finding #7.
    public int? PageIndex { get; init; }

    public int? PageSize { get; init; }

    public string? CustomerName { get; init; }
}

internal sealed class ListPurchaseOrdersQueryValidator : AbstractValidator<ListPurchaseOrdersQuery>
{
    #region Constructors

    public ListPurchaseOrdersQueryValidator()
    {
        RuleFor(a => a.PageSize).InclusiveBetween(1, 100).When(a => a.PageSize.HasValue);
        RuleFor(a => a.PageIndex).GreaterThan(0).When(a => a.PageIndex.HasValue);
    }

    #endregion
}

internal sealed class ListPurchaseOrdersQueryHandler(IRepositorySpec repository, IMapper mapper)
    : Fluents.Queries.IPageHandler<ListPurchaseOrdersQuery, PurchaseOrderDto>
{
    public async Task<IPagedList<PurchaseOrderDto>> OnHandle(
        ListPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var spec = new SpecGetPurchaseOrder(byCustomerName: request.CustomerName);

        var pageIndex = request.PageIndex ?? ListPurchaseOrdersQuery.DefaultPageIndex;
        var pageSize = request.PageSize ?? ListPurchaseOrdersQuery.DefaultPageSize;
        var page = await repository.ToPagedListAsync(spec, pageIndex, pageSize, cancellationToken);

        return new StaticPagedList<PurchaseOrderDto>(page.Select(mapper.Map<PurchaseOrderDto>), page);
    }
}
