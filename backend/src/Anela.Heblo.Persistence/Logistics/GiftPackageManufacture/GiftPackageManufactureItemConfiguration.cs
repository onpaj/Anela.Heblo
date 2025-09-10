using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureItemConfiguration : IEntityTypeConfiguration<GiftPackageManufactureItem>
{
    public void Configure(EntityTypeBuilder<GiftPackageManufactureItem> builder)
    {
        builder.ToTable("gift_package_manufacture_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ManufactureLogId)
            .HasColumnName("manufacture_log_id")
            .IsRequired();

        builder.Property(x => x.ProductCode)
            .HasColumnName("product_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.QuantityConsumed)
            .HasColumnName("quantity_consumed")
            .IsRequired();

        // Index for performance
        builder.HasIndex(x => x.ManufactureLogId)
            .HasDatabaseName("ix_gift_package_manufacture_items_manufacture_log_id");

        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("ix_gift_package_manufacture_items_product_code");
    }
}