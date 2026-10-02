using DKNet.Notification.AppServices.ManualSample.V1.Specs;
using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.App.Tests.Unit.ManualSample;

public class SpecGetPurchaseOrderTests
{
    #region Methods

    [Fact]
    public void NoFilter_ShouldMatchEveryOrder()
    {
        // Regression guard for the DRK-714 bug: an unstarted predicate builder compiled to WHERE FALSE,
        // which made the list endpoint always return an empty page.
        var predicate = new SpecGetPurchaseOrder().FilterQuery!.Compile();

        predicate(MakeOrder("Acme")).ShouldBeTrue();
        predicate(MakeOrder("Globex")).ShouldBeTrue();
    }

    [Fact]
    public void ById_ShouldMatchOnlyThatOrder()
    {
        var order = MakeOrder("Acme");
        var other = MakeOrder("Globex");

        var predicate = new SpecGetPurchaseOrder(byId: order.Id).FilterQuery!.Compile();

        predicate(order).ShouldBeTrue();
        predicate(other).ShouldBeFalse();
    }

    [Fact]
    public void ByCustomerName_ShouldMatchOnlyThatCustomer()
    {
        var predicate = new SpecGetPurchaseOrder(byCustomerName: "Acme").FilterQuery!.Compile();

        predicate(MakeOrder("Acme")).ShouldBeTrue();
        predicate(MakeOrder("Globex")).ShouldBeFalse();
    }

    [Fact]
    public void ByIdAndCustomerName_ShouldRequireBoth()
    {
        var order = MakeOrder("Acme");

        var predicate = new SpecGetPurchaseOrder(byId: order.Id, byCustomerName: "Globex").FilterQuery!.Compile();

        predicate(order).ShouldBeFalse();
    }

    private static PurchaseOrder MakeOrder(string customerName) =>
        new(Guid.NewGuid(), customerName, 10m, "system");

    #endregion
}
