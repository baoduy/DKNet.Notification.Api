using System.ComponentModel.DataAnnotations;
using DKNet.EfCore.Abstractions.Attributes;
using DKNet.EfCore.Abstractions.Events;
using DKNet.EfCore.DataAuthorization;
using DKNet.Notification.Domains.Share;

namespace DKNet.Notification.Domains.Features.AutomatedSample.Entities;

/// <summary>
/// Represents a sellable product aggregate root.
/// </summary>
/// <remarks>
/// Composite-first, not fully generated: creation and price changes are declared via
/// <see cref="RaisesEventAttribute"/> and raised automatically by the DKNet events hook on save — nothing
/// here raises an event by hand. The <c>[CrudCreate]</c> constructor's parameter list becomes the generated
/// create request's payload, so it deliberately carries no acting-user parameter — that would make the
/// acting user caller-settable.
/// A <c>[CrudAction]</c> method publishes a POST at the by-id route plus a segment, returning 200 with the
/// entity DTO. <see cref="Approve"/> overrides the segment (<c>approval</c>) while keeping the default POST
/// verb; <see cref="AssignSupplierReference"/> overrides both the segment (<c>supplier-reference</c>) and
/// the verb (PUT), under its own authorization scope. <see cref="Discontinue"/> keeps its
/// <c>[CrudAction]</c> declaration so the generator still has a route to drop by name, but that generated
/// route is excluded and replaced by a hand-written one — see <see cref="Discontinue"/>'s own remarks.
/// Implements <see cref="IOwnedBy"/> so <c>DataOwnerAuthQuery</c>'s global read filter (row-level isolation)
/// applies to it; <c>DataOwnerHook</c> stamps <see cref="OwnedBy"/> from the same ownership key as
/// <c>CreatedBy</c> on insert.
/// </remarks>
[RaisesEvent(EventOperations.Created, Include = [nameof(Id), nameof(Name), nameof(Price)])]
[RaisesEvent(EventOperations.Updated, nameof(Price))]
[RaisesEvent(EventOperations.Updated, nameof(IsDiscontinued))]
public class Product : AggregateRoot, IOwnedBy
{
    #region Constructors

    /// <summary>
    /// Creates a new <see cref="Product"/>.
    /// </summary>
    /// <param name="name">The product name. Unique.</param>
    /// <param name="price">The unit price. Must be positive.</param>
    /// <param name="supplierCostPrice">The supplier's confidential cost price, if disclosed. Optional.</param>
    [CrudCreate]
    public Product(
        [Required, StringLength(150)] string name,
        [Range(0.01, double.MaxValue)] decimal price,
        decimal? supplierCostPrice = null)
    {
        Name = name;
        Price = price;
        SupplierCostPrice = supplierCostPrice;
    }

    /// <inheritdoc />
    protected Product()
    {
    }

    #endregion

    #region Properties

    /// <summary>Gets the product name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Gets the product's current unit price.</summary>
    public decimal Price { get; private set; }

    /// <summary>Gets whether the product has been discontinued.</summary>
    public bool IsDiscontinued { get; private set; }

    /// <summary>Gets the ownership key of the caller who created this product — stamped by <c>DataOwnerHook</c>.</summary>
    public string OwnedBy { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the supplier's confidential cost price for this product, when disclosed. Declared
    /// <see cref="SensitiveDataAttribute"/> with the "pricing" role — the JSON response omits this property
    /// entirely for any caller who does not hold that role.
    /// </summary>
    [SensitiveData("pricing")]
    public decimal? SupplierCostPrice { get; private set; }

    /// <summary>
    /// Gets the supplier's reference code for this product, when disclosed. Declared
    /// <see cref="SensitiveDataAttribute"/> with no role named — the JSON response omits this property for an
    /// unauthenticated caller, but any authenticated caller receives it.
    /// </summary>
    [SensitiveData]
    public string? SupplierReferenceCode { get; private set; }

    #endregion

    #region Methods

    /// <summary>
    /// Changes the product's price.
    /// </summary>
    /// <param name="price">The new unit price. Must be positive.</param>
    [CrudUpdate]
    public void ChangePrice([Range(0.01, double.MaxValue)] decimal price) => Price = price;

    /// <summary>
    /// Approves the product, stamping the acting user.
    /// </summary>
    /// <param name="byUser">The approving user.</param>
    [CrudAction("approval")]
    public void Approve(string byUser) => SetUpdatedBy(byUser);

    /// <summary>
    /// Discontinues the product. Idempotent at this level — calling it again is a no-op here.
    /// </summary>
    /// <remarks>
    /// This method itself stays a plain <c>[CrudAction]</c> so the generator still emits a route for
    /// <c>CrudMapOptions.Exclude(string[])</c> to drop by name — but that generated route is excluded and
    /// replaced by a hand-written <c>PUT {id}/discontinue</c> (<c>DiscontinueProductCommand</c> in
    /// <c>DKNet.Notification.AppServices</c>), because discontinuing a product now also creates its replacement in the
    /// same transaction, and an operation that writes more than one aggregate in one transaction cannot be
    /// generated. The hand-written command handler — not this method — is what refuses a second call
    /// against an already-discontinued product.
    /// </remarks>
    [CrudAction(Verb = CrudActionVerb.Put)]
    public void Discontinue() => IsDiscontinued = true;

    /// <summary>
    /// Assigns the supplier reference code. Deliberately its own action, not a create-request field: a supplier
    /// reference is assigned by procurement after the product exists, never chosen by the client creating it.
    /// </summary>
    /// <param name="supplierReferenceCode">The supplier's reference code.</param>
    [CrudAction("supplier-reference", Verb = CrudActionVerb.Put)]
    public void AssignSupplierReference([Required, StringLength(50)] string supplierReferenceCode) =>
        SupplierReferenceCode = supplierReferenceCode;

    #endregion
}
