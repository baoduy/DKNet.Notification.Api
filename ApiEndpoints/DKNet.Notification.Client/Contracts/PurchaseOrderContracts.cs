namespace DKNet.Notification.Client.Contracts;

/// <summary>A purchase order as the API returns it.</summary>
public sealed record PurchaseOrderResponse
{
    public Guid Id { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Status { get; init; } = string.Empty;

    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>Body of the create-purchase-order call.</summary>
public sealed record CreatePurchaseOrderRequest
{
    public required string CustomerName { get; init; }

    public required decimal Amount { get; init; }
}

/// <summary>Body of the update-purchase-order call.</summary>
public sealed record UpdatePurchaseOrderRequest
{
    public required decimal Amount { get; init; }
}

/// <summary>Query string of the purchase-order list call.</summary>
public sealed record PurchaseOrderListQuery
{
    public int? PageIndex { get; init; }

    public int? PageSize { get; init; }

    public string? CustomerName { get; init; }
}
