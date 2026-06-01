using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerConfiguration : IEntityTypeConfiguration<MaterialContainer>
{
    public void Configure(EntityTypeBuilder<MaterialContainer> builder)
    {
        builder.ToTable("MaterialContainers", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.MaterialCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.LotCode)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 4);

        builder.Property(x => x.Unit)
            .HasMaxLength(20);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CreatedBy)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(100);

        builder.Property(x => x.Status).IsRequired().HasConversion<int>();

        builder.Property(x => x.PurchaseOrderLineId);

        builder.HasOne<PurchaseOrderLine>()
            .WithMany()
            .HasForeignKey(x => x.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_MaterialContainers_PurchaseOrderLines_PurchaseOrderLineId");

        builder.HasIndex(x => x.PurchaseOrderLineId)
            .HasDatabaseName("IX_MaterialContainers_PurchaseOrderLineId");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("IX_MaterialContainers_Code");

        builder.HasIndex(x => new { x.MaterialCode, x.LotCode })
            .HasDatabaseName("IX_MaterialContainers_MaterialCode_LotCode");

        builder.HasIndex(x => new { x.MaterialCode, x.CreatedAt })
            .HasDatabaseName("IX_MaterialContainers_MaterialCode_CreatedAt");
    }
}
