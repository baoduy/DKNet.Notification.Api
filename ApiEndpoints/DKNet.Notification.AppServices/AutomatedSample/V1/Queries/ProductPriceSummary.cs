using DKNet.EfCore.Specifications.Repositories;
using Microsoft.EntityFrameworkCore;
using DKNet.Notification.AppServices.AutomatedSample.V1.Specs;

namespace DKNet.Notification.AppServices.AutomatedSample.V1.Queries;

/// <summary>Product count and average price across every product the caller can see (discontinued included — Q1).</summary>
public sealed record ProductPriceSummaryDto(int ProductCount, decimal AveragePrice);

public sealed record ProductPriceSummaryQuery : Fluents.Queries.IWitResponse<ProductPriceSummaryDto>;

internal sealed class ProductPriceSummaryQueryHandler(IRepositorySpec repository)
    : Fluents.Queries.IHandler<ProductPriceSummaryQuery, ProductPriceSummaryDto>
{
    #region Methods

    public async Task<ProductPriceSummaryDto?> OnHandle(
        ProductPriceSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var products = repository.Query(new SpecGetProduct());

        var count = await products.CountAsync(cancellationToken);
        var averagePrice = count == 0 ? 0m : await products.AverageAsync(p => p.Price, cancellationToken);

        return new ProductPriceSummaryDto(count, averagePrice);
    }

    #endregion
}
