using DKNet.Notification.Domains.Features.AutomatedSample.Entities;

namespace DKNet.Notification.AppServices.AutomatedSample.V1;

/// <summary>
/// Hand-written Mapster customisation for <see cref="ProductDto.GrossMargin"/>, a value the
/// generator's name-matching convention cannot produce.
/// </summary>
/// <remarks>
/// <c>ForType</c> merges onto the convention config <see cref="MapsToExtensions.ScanMaps"/> already
/// built for <c>Product</c> → <see cref="ProductDto"/>; it does not replace it the way <c>NewConfig</c>
/// would, so every convention-mapped property stays untouched. The mapping expression is a plain
/// property subtraction, so it stays EF-translatable when the list route projects it over
/// <c>IQueryable&lt;Product&gt;</c>.
/// </remarks>
internal sealed class ProductMappingRegister : IRegister
{
    #region Methods

    public void Register(TypeAdapterConfig config) =>
        config.ForType<Product, ProductDto>()
            .Map(d => d.GrossMargin, s => s.Price - s.SupplierCostPrice);

    #endregion
}
