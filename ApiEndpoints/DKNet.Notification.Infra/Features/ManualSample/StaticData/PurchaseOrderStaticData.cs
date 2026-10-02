using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.Infra.Features.ManualSample.StaticData;

internal sealed class PurchaseOrderStaticData : DataSeedingConfiguration<PurchaseOrder>
{
    protected override ValueTask<ICollection<PurchaseOrder>> GetDataAsync(
        CancellationToken cancellation = new())
    {
        return ValueTask.FromResult<ICollection<PurchaseOrder>>(
        [
            new PurchaseOrder(
                new Guid("6E6F4D3C-1B7E-4C7A-9F1D-8A2B5C6D7E01"),
                "Acme Pte Ltd",
                1250.00m,
                SharedConsts.SystemAccount),
            new PurchaseOrder(
                new Guid("6E6F4D3C-1B7E-4C7A-9F1D-8A2B5C6D7E02"),
                "Globex Corporation",
                875.50m,
                SharedConsts.SystemAccount),
            new PurchaseOrder(
                new Guid("6E6F4D3C-1B7E-4C7A-9F1D-8A2B5C6D7E03"),
                "Initech LLC",
                430.25m,
                SharedConsts.SystemAccount)
        ]);
    }
}
