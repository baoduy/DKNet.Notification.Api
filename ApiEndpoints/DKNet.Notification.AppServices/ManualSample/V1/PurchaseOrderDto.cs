using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.AppServices.ManualSample.V1;

/// <summary>
/// Hand-written projection of <see cref="PurchaseOrder"/> — no DTO-generation attribute.
/// </summary>
public sealed record PurchaseOrderDto
{
    public Guid Id { get; init; }

    public string CustomerName { get; init; } = null!;

    public decimal Amount { get; init; }

    public PurchaseOrderStatus Status { get; init; }

    public string CreatedBy { get; init; } = null!;
}
