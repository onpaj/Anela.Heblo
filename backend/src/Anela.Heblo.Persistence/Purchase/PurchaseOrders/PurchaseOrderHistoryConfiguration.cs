using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Purchase.PurchaseOrders;

public class PurchaseOrderHistoryConfiguration : IEntityTypeConfiguration<PurchaseOrderHistory>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderHistory> builder)
    {
        builder.ToTable("PurchaseOrderHistory", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.PurchaseOrderId)
            .IsRequired();

        builder.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(PurchaseOrderConstants.ActionMaxLength);

        builder.Property(x => x.OldValue)
            .HasMaxLength(PurchaseOrderConstants.ValueMaxLength);

        builder.Property(x => x.NewValue)
            .HasMaxLength(PurchaseOrderConstants.ValueMaxLength);

        builder.Property(x => x.ChangedBy)
            .IsRequired()
            .HasMaxLength(PurchaseOrderConstants.UserNameMaxLength);

        builder.Property(x => x.ChangedAt)
            .IsRequired()
            .HasColumnType("timestamp");

        builder.HasIndex(x => x.PurchaseOrderId);
        builder.HasIndex(x => x.ChangedAt);
    }
}