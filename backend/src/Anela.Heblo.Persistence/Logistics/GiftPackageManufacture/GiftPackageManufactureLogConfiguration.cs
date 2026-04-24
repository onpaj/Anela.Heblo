using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureLogConfiguration : IEntityTypeConfiguration<GiftPackageManufactureLog>
{
    public void Configure(EntityTypeBuilder<GiftPackageManufactureLog> builder)
    {
        builder.ToTable("GiftPackageManufactureLogs", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.GiftPackageCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.QuantityCreated)
            .IsRequired();

        builder.Property(x => x.StockOverrideApplied)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .IsRequired();

        builder.Property(x => x.OperationType)
            .HasConversion<int>()
            .IsRequired();

        // Navigation property
        builder.HasMany(x => x.ConsumedItems)
            .WithOne(x => x.ManufactureLog)
            .HasForeignKey(x => x.ManufactureLogId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for performance
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_GiftPackageManufactureLogs_CreatedAt");

        builder.HasIndex(x => x.GiftPackageCode)
            .HasDatabaseName("IX_GiftPackageManufactureLogs_GiftPackageCode");

        builder.HasIndex(x => x.OperationType)
            .HasDatabaseName("IX_GiftPackageManufactureLogs_OperationType");
    }
}