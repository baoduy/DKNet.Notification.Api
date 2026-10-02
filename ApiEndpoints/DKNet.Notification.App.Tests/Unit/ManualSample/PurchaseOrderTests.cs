using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.App.Tests.Unit.ManualSample;

public class PurchaseOrderTests
{
    #region Methods

    [Fact]
    public void Ctor_ShouldSetPropertiesAndInitialStatus()
    {
        var order = new PurchaseOrder("Acme Pte Ltd", 250.00m, "alice");

        order.CustomerName.ShouldBe("Acme Pte Ltd");
        order.Amount.ShouldBe(250.00m);
        order.Status.ShouldBe(PurchaseOrderStatus.Placed);
        order.CreatedBy.ShouldBe("alice");
        order.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Ctor_ShouldRaisePurchaseOrderCreatedEvent_WithMatchingPayload()
    {
        var order = new PurchaseOrder("Acme Pte Ltd", 250.00m, "alice");

        var (events, _) = order.GetEvents();
        var raised = events.OfType<PurchaseOrderCreatedEvent>().Single();

        raised.Id.ShouldBe(order.Id);
        raised.CustomerName.ShouldBe("Acme Pte Ltd");
        raised.Amount.ShouldBe(250.00m);
    }

    [Fact]
    public void RehydrationCtor_UsedByStaticSeeding_ShouldNotRaiseCreatedEvent()
    {
        var id = Guid.NewGuid();

        var order = new PurchaseOrder(id, "Acme Pte Ltd", 1250.00m, "System");

        order.Id.ShouldBe(id);
        order.CreatedBy.ShouldBe("System");
        var (events, eventTypes) = order.GetEvents();
        events.ShouldBeEmpty();
        eventTypes.ShouldBeEmpty();
    }

    [Fact]
    public void ChangeAmount_ShouldUpdateAmount_AndStampUpdatedBy()
    {
        var order = new PurchaseOrder("Acme Pte Ltd", 100m, "alice");

        order.ChangeAmount(999.99m, "bob");

        order.Amount.ShouldBe(999.99m);
        order.UpdatedBy.ShouldBe("bob");
    }

    [Fact]
    public void Cancel_ShouldSetStatusCancelled_AndStampUpdatedBy()
    {
        var order = new PurchaseOrder("Acme Pte Ltd", 100m, "alice");

        order.Cancel("bob");

        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        order.UpdatedBy.ShouldBe("bob");
    }

    #endregion
}
