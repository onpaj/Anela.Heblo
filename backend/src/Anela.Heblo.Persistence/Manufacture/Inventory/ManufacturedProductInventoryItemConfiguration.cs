using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryItemConfiguration : IEntityTypeConfiguration<ManufacturedProductInventoryItem>
{
    public void Configure(EntityTypeBuilder<ManufacturedProductInventoryItem> builder)
    {
        builder.ToTable("ManufacturedProductInventoryItems", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ProductCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.LotNumber).HasMaxLength(100);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(x => x.LastModifiedAt)
            .HasColumnType("timestamp without time zone");
        builder.Property(x => x.LastModifiedBy).HasMaxLength(500);

        builder.HasMany(x => x.Log)
            .WithOne()
            .HasForeignKey(l => l.InventoryItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("IX_ManufacturedProductInventoryItems_ProductCode");
        builder.HasIndex(new[] { "ProductCode", "LotNumber" })
            .HasDatabaseName("IX_ManufacturedProductInventoryItems_ProductCode_LotNumber");
        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufacturedProductInventoryItems_ManufactureOrderId");
    }
}
