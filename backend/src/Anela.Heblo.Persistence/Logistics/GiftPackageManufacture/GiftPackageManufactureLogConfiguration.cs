using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureLogConfiguration : IEntityTypeConfiguration<GiftPackageManufactureLog>
{
    public void Configure(EntityTypeBuilder<GiftPackageManufactureLog> builder)
    {
        builder.ToTable("gift_package_manufacture_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.GiftPackageCode)
            .HasColumnName("gift_package_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.QuantityCreated)
            .HasColumnName("quantity_created")
            .IsRequired();

        builder.Property(x => x.StockOverrideApplied)
            .HasColumnName("stock_override_applied")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        // Navigation property
        builder.HasMany(x => x.ConsumedItems)
            .WithOne(x => x.ManufactureLog)
            .HasForeignKey(x => x.ManufactureLogId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for performance
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_gift_package_manufacture_logs_created_at");

        builder.HasIndex(x => x.GiftPackageCode)
            .HasDatabaseName("ix_gift_package_manufacture_logs_gift_package_code");
    }
}