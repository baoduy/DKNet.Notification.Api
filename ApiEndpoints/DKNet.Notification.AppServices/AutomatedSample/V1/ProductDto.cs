using DKNet.EfCore.Abstractions.Entities;
using DKNet.EfCore.Abstractions.Attributes;
using DKNet.EfCore.DtoGenerator;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;

namespace DKNet.Notification.AppServices.AutomatedSample.V1;

/// <summary>DTO returned by the generated <see cref="Product"/> CRUD endpoints.</summary>
/// <remarks>
/// <see cref="Product.OwnedBy"/> is the same ownership key already exposed as <c>CreatedBy</c> —
/// excluded here so the API surface doesn't carry it twice.
/// <para>
/// <c>LastModifiedBy</c>/<c>LastModifiedOn</c> are excluded too: on <see cref="AuditedEntity{TKey}"/> they
/// are computed conveniences (the updated value, or the created one if never modified), not mapped columns.
/// Left on the DTO they break the generic list route — its filter/search/order build EF predicates against
/// the entity by property name, and an unmapped member makes the whole query fail to translate. They
/// duplicate <c>UpdatedBy</c>/<c>UpdatedOn</c>, which stay.
/// </para>
/// </remarks>
[GenerateDto(typeof(Product),
    Exclude = [nameof(Product.OwnedBy), nameof(AuditedEntity<Guid>.LastModifiedBy), nameof(AuditedEntity<Guid>.LastModifiedOn)])]
public sealed partial record ProductDto
{
    /// <summary>
    /// Gets the product's gross margin (<see cref="Product.Price"/> minus
    /// <see cref="Product.SupplierCostPrice"/>).
    /// </summary>
    /// <remarks>
    /// The generator's name-matching convention only mirrors an entity property onto the DTO under the
    /// same (or flexibly-matched) name — it cannot produce a value derived from two source properties. A
    /// margin, a total, or any other computed-from-multiple-columns value falls in that class and needs a
    /// hand-written Mapster rule (<see cref="ProductMappingRegister"/>) instead. Every other property on
    /// this DTO — <c>Name</c>, <c>Price</c>, <c>SupplierCostPrice</c>, the audit columns — still comes from
    /// the generator's convention untouched; only this one property is hand-mapped.
    /// <see cref="SensitiveDataAttribute"/> "pricing" mirrors <see cref="Product.SupplierCostPrice"/>'s own
    /// restriction, since the margin discloses the same confidential value.
    /// </remarks>
    [SensitiveData("pricing")]
    public decimal? GrossMargin { get; init; }
}
