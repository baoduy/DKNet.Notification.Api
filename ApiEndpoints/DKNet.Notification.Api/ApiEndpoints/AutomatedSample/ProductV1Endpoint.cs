using DKNet.AspCore.Extensions.Responses;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using DKNet.Notification.AppServices.AutomatedSample.V1;
using DKNet.Notification.AppServices.AutomatedSample.V1.Actions;
using DKNet.Notification.AppServices.AutomatedSample.V1.Queries;
using DKNet.Notification.AppServices.Crud;

namespace DKNet.Notification.Api.ApiEndpoints.AutomatedSample;

/// <summary>
/// Composite-first: the generated CRUD slice for <c>Product</c> is mapped first, then the two hand-written
/// business routes below it — one replacing a generated route the composite rule outgrew, one with no
/// generated shape at all.
/// <para>
/// Authorization is declared once per HTTP method with <see cref="EndpointGroupScopeAttribute"/> below,
/// covering generated and hand-mapped routes alike. The attribute is applied only when
/// <c>EndpointRegistrationOptions.RequireAuthorization</c> is on (Program.cs assigns it from
/// <see cref="FeatureOptions.RequireAuthorization"/>), so it needs no flag check of its own. Only the two
/// PUT routes whose scope their HTTP method cannot decide are overridden per route — and those calls do
/// not self-gate, so they stay behind the flag read in <see cref="Map"/>.
/// </para>
/// </summary>
[EndpointGroupScope(ProductScopes.Read, EndpointHttpMethods.Get)]
[EndpointGroupScope(ProductScopes.Write, EndpointHttpMethods.Post, EndpointHttpMethods.Put,
    EndpointHttpMethods.Delete)]
internal sealed class ProductV1Endpoint : IEndpointConfig
{
    #region Properties

    public int Version => 1;

    public string GroupEndpoint => "/products";

    #endregion

    #region Methods

    public void Map(RouteGroupBuilder group)
    {
        // R2: a route's authorization metadata resolves against policies AddAuthConfig registers. With
        // RequireAuthorization off, the host never calls AddAuthConfig, so a bare RequireAuthorization(...)
        // would throw at request time. The class-level [EndpointGroupScope] declarations are applied only
        // when the flag is on and need no guard; the two per-route overrides below do, so the flag is read
        // once here for them.
        var requireAuthorization = ((IEndpointRouteBuilder)group).ServiceProvider
            .GetRequiredService<IOptions<FeatureOptions>>().Value.RequireAuthorization;

        group.WithDescription(
            "Automated sample — Product CRUD generated from [CrudCreate]/[CrudUpdate]/[RaisesEvent], " +
            "plus two hand-written business routes below it.");

        group.MapProductCrud(o =>
        {
            // Dropped by name and replaced below — see that route's comment for why (R1).
            o.Exclude("Discontinue");

            // Every other generated route takes its scope from the group declarations: GET -> products.read,
            // POST/PUT/DELETE -> products.write. Only this one needs its own — it is a PUT like Update, so no
            // per-method declaration can separate the two, and holding products.write must not be enough to
            // assign a supplier reference. A route that names its own policy is left alone by the group rule.
            if (requireAuthorization)
            {
                o.Configure("AssignSupplierReference", rb => rb.RequireAuthorization(ProductScopes.Supplier));
            }
        });

        // An operation that writes more than one aggregate in one transaction cannot be generated:
        // discontinuing a product also creates its named replacement, so this route is hand-written and
        // dropped from the generated block above by name rather than left to the generator.
        var discontinue = group.MapPut("{id:guid}/discontinue", async (
                Guid id,
                DiscontinueProductCommand req,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var result = await bus.Send(req with { Id = id }, cancellationToken: ct);
                return result.Response();
            })
            .Produces<ProductDto>()
            .WithDescription("Discontinue a product and create its named replacement in the same transaction.");

        // A PUT like Update and AssignSupplierReference, but with its own scope — the group's PUT
        // declaration cannot separate the three, so this one names its policy explicitly.
        if (requireAuthorization)
        {
            discontinue.RequireAuthorization(ProductScopes.Discontinue);
        }

        // No scope call here: this is a GET, so the group's products.read declaration covers it.
        group.MapGet("summary", async (
                IMessageBus bus,
                CancellationToken ct) =>
            {
                var result = await bus.Send(new ProductPriceSummaryQuery(), cancellationToken: ct);
                return Results.Ok(result);
            })
            .Produces<ProductPriceSummaryDto>()
            .WithDescription("Product count and average price across every product the caller can see.");
    }

    #endregion
}
