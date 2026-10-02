using DKNet.Notification.Domains.Features.ManualSample.Entities;

namespace DKNet.Notification.Infra.Features.ManualSample.Mappers;

internal sealed class PurchaseOrderConfigs : DefaultEntityTypeConfiguration<PurchaseOrder>
{
    #region Methods

    public override void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        base.Configure(builder);

        builder.HasIndex(p => p.CustomerName);
        builder.Property(p => p.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>();
        builder.ToTable("PurchaseOrders", "manual_sample");
    }

    #endregion
}
