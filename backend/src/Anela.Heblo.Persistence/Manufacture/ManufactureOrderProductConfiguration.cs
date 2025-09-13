using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderProductConfiguration : IEntityTypeConfiguration<ManufactureOrderProduct>
{
    public void Configure(EntityTypeBuilder<ManufactureOrderProduct> builder)
    {
        builder.ToTable("ManufactureOrderProducts", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProductCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ProductName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.SemiProductCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.PlannedQuantity)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.ActualQuantity)
            .IsRequired()
            .HasPrecision(18, 4);

        // Indexes for performance
        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufactureOrderProducts_ManufactureOrderId");

        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("IX_ManufactureOrderProducts_ProductCode");

        builder.HasIndex(x => x.SemiProductCode)
            .HasDatabaseName("IX_ManufactureOrderProducts_SemiProductCode");
    }
}