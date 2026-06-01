using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureItemConfiguration : IEntityTypeConfiguration<GiftPackageManufactureItem>
{
    public void Configure(EntityTypeBuilder<GiftPackageManufactureItem> builder)
    {
        builder.ToTable("GiftPackageManufactureItems", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ManufactureLogId)
            .IsRequired();

        builder.Property(x => x.ProductCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.QuantityConsumed)
            .IsRequired();

        // Index for performance
        builder.HasIndex(x => x.ManufactureLogId)
            .HasDatabaseName("IX_GiftPackageManufactureItems_ManufactureLogId");

        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("IX_GiftPackageManufactureItems_ProductCode");
    }
}