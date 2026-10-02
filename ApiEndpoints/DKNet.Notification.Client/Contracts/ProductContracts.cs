using Refit;

namespace DKNet.Notification.Client.Contracts;

/// <summary>A product as the API returns it.</summary>
public sealed record ProductResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public bool IsDiscontinued { get; init; }

    public decimal? SupplierCostPrice { get; init; }

    public decimal? GrossMargin { get; init; }

    public string? SupplierReferenceCode { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedOn { get; init; }

    public string? UpdatedBy { get; init; }

    public DateTimeOffset? UpdatedOn { get; init; }
}

/// <summary>Product count and average price across every product the caller can see.</summary>
public sealed record ProductPriceSummaryResponse(int ProductCount, decimal AveragePrice);

/// <summary>Body of the create-product call.</summary>
public sealed record CreateProductRequest
{
    public required string Name { get; init; }

    public required decimal Price { get; init; }

    public decimal? SupplierCostPrice { get; init; }
}

/// <summary>Body of the change-price call.</summary>
public sealed record ChangeProductPriceRequest
{
    public required decimal Price { get; init; }
}

/// <summary>Body of the approve-product call.</summary>
public sealed record ApproveProductRequest
{
    public required string ByUser { get; init; }
}

/// <summary>Body of the assign-supplier-reference call.</summary>
public sealed record AssignSupplierReferenceRequest
{
    public required string SupplierReferenceCode { get; init; }
}

/// <summary>Body of the discontinue-product call: the replacement created in the same transaction.</summary>
public sealed record DiscontinueProductRequest
{
    public required string ReplacementName { get; init; }

    public required decimal ReplacementPrice { get; init; }
}

/// <summary>Query string of the product list call.</summary>
public sealed record ProductListQuery
{
    /// <summary>Filter conditions (<c>field:Operator:value</c>), all of which must hold.</summary>
    [Query(CollectionFormat.Multi)]
    public IReadOnlyList<string>? Filter { get; init; }

    public string? Search { get; init; }

    public string? OrderBy { get; init; }

    public bool? Desc { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    [Query(Format = "O")]
    public DateTimeOffset? FromDate { get; init; }

    [Query(Format = "O")]
    public DateTimeOffset? ToDate { get; init; }
}
