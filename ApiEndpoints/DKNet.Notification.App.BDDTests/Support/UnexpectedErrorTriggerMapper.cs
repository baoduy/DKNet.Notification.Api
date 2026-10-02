using Mapster;
using MapsterMapper;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// Test-only <see cref="IMapper" /> that raises a genuine unhandled exception when mapping a
/// <see cref="PurchaseOrder" /> named <see cref="TriggerValue" /> — the BDD suite's only reachable seam for
/// "an unexpected error" (DRK-1515 §3 row 3). The request still goes through a real, existing template route
/// (cancel), and the exception is raised from inside the handler that route already calls
/// (<c>CancelPurchaseOrderCommandHandler</c>), so it still flows through the app's own exception-handling
/// middleware rather than bypassing it.
/// </summary>
public sealed class UnexpectedErrorTriggerMapper(IServiceProvider serviceProvider, TypeAdapterConfig config)
    : ServiceMapper(serviceProvider, config)
{
    public const string TriggerValue = "__drk1515-unexpected-error-trigger__";

    public override TDestination Map<TDestination>(object source)
    {
        if (source is PurchaseOrder { CustomerName: TriggerValue })
        {
            throw new InvalidOperationException("DRK-1515 test seam: deliberate unexpected error.");
        }

        return base.Map<TDestination>(source);
    }
}
