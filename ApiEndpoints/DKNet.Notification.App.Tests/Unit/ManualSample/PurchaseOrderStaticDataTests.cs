using System.Reflection;
using DKNet.Notification.Domains.Features.ManualSample.Entities;
using DKNet.Notification.Infra.Features.ManualSample.StaticData;
using DKNet.Notification.Share;

namespace DKNet.Notification.App.Tests.Unit.ManualSample;

/// <summary>
/// <see cref="PurchaseOrderStaticData"/>'s <c>GetDataAsync</c> is <c>protected</c> on a <c>sealed</c> class,
/// invoked only by DKNet's <c>UseAutoDataSeeding</c> model-building pipeline — nothing public exposes it for
/// a direct call. Neither the xUnit ApiFixture nor the BDD BddApiFactory wires <c>.UseAutoDataSeeding(...)</c>
/// into their InMemory DbContext options (that wiring lives only in the real app's composition root), so this
/// class is otherwise never exercised by any test in the suite. Invoke it via reflection to prove the actual
/// seed data it produces, rather than leaving it at 0% coverage.
/// </summary>
public class PurchaseOrderStaticDataTests
{
    [Fact]
    public async Task GetDataAsync_ShouldReturnThreeFixedReferenceOrders_OwnedBySystemAccount()
    {
        var seeding = new PurchaseOrderStaticData();
        var method = typeof(PurchaseOrderStaticData).GetMethod("GetDataAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var task = (ValueTask<ICollection<PurchaseOrder>>)method.Invoke(seeding, [CancellationToken.None])!;
        var orders = await task;

        orders.Count.ShouldBe(3);
        orders.ShouldContain(o => o.CustomerName == "Acme Pte Ltd" && o.Amount == 1250.00m);
        orders.ShouldContain(o => o.CustomerName == "Globex Corporation" && o.Amount == 875.50m);
        orders.ShouldContain(o => o.CustomerName == "Initech LLC" && o.Amount == 430.25m);
        orders.ShouldAllBe(o => o.CreatedBy == SharedConsts.SystemAccount);
        orders.ShouldAllBe(o => o.Status == PurchaseOrderStatus.Placed);
        orders.Select(o => o.Id).Distinct().Count().ShouldBe(3);
    }
}
