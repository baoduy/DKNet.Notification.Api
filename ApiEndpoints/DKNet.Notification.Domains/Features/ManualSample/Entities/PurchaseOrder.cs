using DKNet.Notification.Domains.Share;

namespace DKNet.Notification.Domains.Features.ManualSample.Entities;

/// <summary>
/// The lifecycle state of a <see cref="PurchaseOrder"/>.
/// </summary>
public enum PurchaseOrderStatus
{
    Draft,
    Placed,
    Cancelled
}

/// <summary>
/// Purchase order aggregate root. Every layer of this sample — including this entity's event — is hand-written;
/// no declarative event/CRUD/DTO-generation attribute is used anywhere.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot
{
    #region Constructors

    public PurchaseOrder(string customerName, decimal amount, string byUser)
        : base(byUser)
    {
        CustomerName = customerName;
        Amount = amount;
        Status = PurchaseOrderStatus.Placed;

        AddEvent(new PurchaseOrderCreatedEvent(Id, CustomerName, Amount));
    }

    /// <summary>
    /// Rehydrates a <see cref="PurchaseOrder"/> with a known identity — used by static reference-data seeding only.
    /// Does not re-raise <see cref="PurchaseOrderCreatedEvent"/>.
    /// </summary>
    internal PurchaseOrder(Guid id, string customerName, decimal amount, string byUser)
        : base(id, byUser)
    {
        CustomerName = customerName;
        Amount = amount;
        Status = PurchaseOrderStatus.Placed;
    }

    private PurchaseOrder()
    {
    }

    #endregion

    #region Properties

    public string CustomerName { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    #endregion

    #region Methods

    public void ChangeAmount(decimal amount, string userId)
    {
        Amount = amount;
        SetUpdatedBy(userId);
    }

    public void Cancel(string userId)
    {
        Status = PurchaseOrderStatus.Cancelled;
        SetUpdatedBy(userId);
    }

    #endregion
}
