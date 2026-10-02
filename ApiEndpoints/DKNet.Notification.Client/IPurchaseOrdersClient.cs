using DKNet.Notification.Client.Contracts;
using Refit;

namespace DKNet.Notification.Client;

/// <summary>Typed client for the Purchase Orders endpoints (manual sample) of the API.</summary>
/// <remarks>A failed call throws <see cref="ApiException" />, which carries the status code and the
/// problem details the API returned.</remarks>
public interface IPurchaseOrdersClient
{
    /// <summary>Creates a purchase order. <paramref name="idempotencyKey" /> is required and is sent as
    /// the <c>X-Idempotency-Key</c> header; a null or blank key is refused before any request is sent, with an
    /// <see cref="ApiRequestException" /> whose <see cref="Exception.InnerException" /> is the
    /// <see cref="ArgumentException" /> naming the missing header.</summary>
    [Post("/v1/purchase-orders")]
    Task<PurchaseOrderResponse> CreatePurchaseOrderAsync(
        [Body] CreatePurchaseOrderRequest request,
        [Header(IdempotencyKeyGuard.HeaderName)] string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one page of purchase orders, optionally filtered by customer name. The API returns the
    /// page's items only, with no paging envelope.</summary>
    [Get("/v1/purchase-orders")]
    Task<IReadOnlyList<PurchaseOrderResponse>> ListPurchaseOrdersAsync(
        [Query] PurchaseOrderListQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one purchase order by id.</summary>
    [Get("/v1/purchase-orders/{id}")]
    Task<PurchaseOrderResponse> GetPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Updates the amount of a purchase order.</summary>
    [Put("/v1/purchase-orders/{id}")]
    Task<PurchaseOrderResponse> UpdatePurchaseOrderAsync(
        Guid id,
        [Body] UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels a purchase order.</summary>
    [Post("/v1/purchase-orders/{id}/cancel")]
    Task<PurchaseOrderResponse> CancelPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a purchase order.</summary>
    [Delete("/v1/purchase-orders/{id}")]
    Task DeletePurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default);
}
