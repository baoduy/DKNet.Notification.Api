using DKNet.EfCore.Specifications.Definitions;
using DKNet.Notification.Domains.Features.AutomatedSample.Entities;

namespace DKNet.Notification.AppServices.AutomatedSample.V1.Specs;

internal sealed class SpecProductByName : Specification<Product>
{
    public SpecProductByName(string name)
    {
        WithFilter(CreatePredicate().And(p => p.Name == name));
    }
}
