using DKNet.AspCore.Extensions.Responses;
using DKNet.AspCore.Idempotency;
using DKNet.Notification.AppServices.ManualSample.V1.Actions;
using DKNet.Notification.AppServices.ManualSample.V1.Queries;
using PurchaseOrderDto = DKNet.Notification.AppServices.ManualSample.V1.PurchaseOrderDto;

namespace DKNet.Notification.Api.ApiEndpoints.ManualSample;

/// <summary>
/// Every route here is written with the raw dknet.notification-API surface — no generic entity/DTO route-registration
/// helper is used (see DRK-714 §4). The acting user comes only from <c>[FromClaim]</c> and
/// <c>AddContextualRequestPopulation</c>; this endpoint never stamps it itself.
/// </summary>
internal sealed class PurchaseOrderV1Endpoint : IEndpointConfig
{
    #region Properties

    public int Version => 1;

    public string GroupEndpoint => "/purchase-orders";

    #endregion

    #region Methods

    public void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
                CreatePurchaseOrderRequest req,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var result = await bus.Send(req, cancellationToken: ct);
                return result.Response(isCreated: true);
            })
            .RequiredIdempotentKey()
            .Produces<PurchaseOrderDto>(StatusCodes.Status201Created)
            .WithDescription(
                "Create purchase order. <br/><br/> Note: Idempotency key is required in the header. <br/>" +
                "X-Idempotency-Key: {IdempotencyKey} <br/>");

        group.MapGet("/", async (
                [AsParameters] ListPurchaseOrdersQuery query,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var page = await bus.Send(query, cancellationToken: ct);
                return Results.Ok(page);
            })
            .WithDescription("Get purchase orders (paged, optionally filtered by customer name).");

        group.MapGet("{id:guid}", async (
                Guid id,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var dto = await bus.Send(new GetPurchaseOrderByIdQuery { Id = id }, cancellationToken: ct);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .Produces<PurchaseOrderDto>()
            .Produces(StatusCodes.Status404NotFound)
            .WithDescription("Get purchase order by id");

        group.MapPut("{id:guid}", async (
                Guid id,
                UpdatePurchaseOrderRequest req,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var result = await bus.Send(req with { Id = id }, cancellationToken: ct);
                return result.Response();
            })
            .WithDescription("Update purchase order amount");

        group.MapPost("{id:guid}/cancel", async (
                [AsParameters] CancelPurchaseOrderRequest req,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var result = await bus.Send(req, cancellationToken: ct);
                return result.Response();
            })
            .WithDescription("Cancel purchase order");

        group.MapDelete("{id:guid}", async (
                [AsParameters] DeletePurchaseOrderRequest req,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var result = await bus.Send(req, cancellationToken: ct);
                return result.Response();
            })
            .WithDescription("Delete purchase order");
    }

    #endregion
}
