using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceChangeLogConfiguration : IEntityTypeConfiguration<ProductPriceChangeLog>
{
    public void Configure(EntityTypeBuilder<ProductPriceChangeLog> builder)
    {
        builder.ToTable("ProductPriceChangeLogs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ProductCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.OldPriceWithVat).HasPrecision(18, 2);
        builder.Property(e => e.NewPriceWithVat).HasPrecision(18, 2);
        builder.Property(e => e.ChangedBy).HasMaxLength(256).IsRequired();
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(e => new { e.ProductCode, e.ChangedAt });
    }
}
