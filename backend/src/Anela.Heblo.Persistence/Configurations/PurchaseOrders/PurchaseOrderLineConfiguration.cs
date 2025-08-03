using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Configurations.PurchaseOrders;

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLines", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.PurchaseOrderId)
            .IsRequired();

        builder.Property(x => x.MaterialId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Quantity)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.UnitPrice)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.Notes)
            .HasMaxLength(PurchaseOrderConstants.NotesMaxLength);

        builder.Ignore(x => x.LineTotal);

        builder.HasIndex(x => x.PurchaseOrderId);
        builder.HasIndex(x => x.MaterialId);
    }
}