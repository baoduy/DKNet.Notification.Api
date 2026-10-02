using DKNet.Notification.Client.Contracts;
using Refit;

namespace DKNet.Notification.Client;

/// <summary>Typed client for the Products endpoints (automated sample) of the API.</summary>
/// <remarks>
/// One method per endpoint the API maps for products — the generated CRUD routes plus the hand-written
/// discontinue and summary routes. A failed call throws <see cref="ApiException" />, which carries the
/// status code and the problem details the API returned.
/// </remarks>
public interface IProductsClient
{
    /// <summary>Gets one product by id.</summary>
    [Get("/v1/products/{id}")]
    Task<ProductResponse> GetProductAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets one page of products, optionally filtered, searched and ordered.</summary>
    [Get("/v1/products")]
    Task<PagedResponse<ProductResponse>> ListProductsAsync(
        [Query] ProductListQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a product.</summary>
    [Post("/v1/products")]
    Task<ProductResponse> CreateProductAsync(
        [Body] CreateProductRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the price of a product.</summary>
    [Put("/v1/products/{id}")]
    Task<ProductResponse> ChangeProductPriceAsync(
        Guid id,
        [Body] ChangeProductPriceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a product.</summary>
    [Delete("/v1/products/{id}")]
    Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Approves a product.</summary>
    [Post("/v1/products/{id}/approval")]
    Task<ProductResponse> ApproveProductAsync(
        Guid id,
        [Body] ApproveProductRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Assigns the supplier reference code of a product.</summary>
    [Put("/v1/products/{id}/supplier-reference")]
    Task<ProductResponse> AssignSupplierReferenceAsync(
        Guid id,
        [Body] AssignSupplierReferenceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Discontinues a product and creates its named replacement in the same transaction.</summary>
    [Put("/v1/products/{id}/discontinue")]
    Task<ProductResponse> DiscontinueProductAsync(
        Guid id,
        [Body] DiscontinueProductRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the product count and average price across every product the caller can see.</summary>
    [Get("/v1/products/summary")]
    Task<ProductPriceSummaryResponse> GetProductSummaryAsync(CancellationToken cancellationToken = default);
}
