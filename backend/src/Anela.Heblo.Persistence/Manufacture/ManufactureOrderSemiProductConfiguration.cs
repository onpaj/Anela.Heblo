using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderSemiProductConfiguration : IEntityTypeConfiguration<ManufactureOrderSemiProduct>
{
    public void Configure(EntityTypeBuilder<ManufactureOrderSemiProduct> builder)
    {
        builder.ToTable("ManufactureOrderSemiProducts", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProductCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ProductName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PlannedQuantity)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.ActualQuantity)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.LotNumber)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(x => x.ExpirationDate)
            .IsRequired(false);

        // Indexes for performance
        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufactureOrderSemiProducts_ManufactureOrderId");

        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("IX_ManufactureOrderSemiProducts_ProductCode");
    }
}