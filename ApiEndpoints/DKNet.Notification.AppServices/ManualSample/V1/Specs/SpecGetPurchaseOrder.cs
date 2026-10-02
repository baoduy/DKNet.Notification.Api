using DKNet.EfCore.Specifications.Definitions;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.AppServices.ManualSample.V1.Specs;

internal sealed class SpecGetPurchaseOrder : Specification<PurchaseOrder>
{
    public SpecGetPurchaseOrder(Guid? byId = null, string? byCustomerName = null)
    {
        var predicator = CreatePredicate();

        if (byId is not null)
        {
            predicator = predicator.And(a => a.Id == byId);
        }

        if (!string.IsNullOrEmpty(byCustomerName))
        {
            predicator = predicator.And(a => a.CustomerName == byCustomerName);
        }

        if (byId is null && string.IsNullOrEmpty(byCustomerName))
        {
            // An unstarted predicate builder compiles to WHERE FALSE — without this, "no filter" would
            // silently match nothing instead of listing every order.
            predicator = predicator.And(_ => true);
        }

        WithFilter(predicator);
    }
}
