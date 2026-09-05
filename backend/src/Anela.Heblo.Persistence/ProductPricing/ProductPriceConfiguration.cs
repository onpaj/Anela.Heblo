using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.ToTable("ProductPrices", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("ProductCode")
            .IsRequired()
            .HasMaxLength(50);

        builder.Ignore(e => e.ProductCode);
        builder.Ignore(e => e.PriceWithoutVat);

        builder.Property(e => e.PriceWithVat)
            .IsRequired()
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.VatRate)
            .IsRequired()
            .HasColumnType("decimal(5,2)");

        builder.Property(e => e.ModifiedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ModifiedBy)
            .IsRequired()
            .HasMaxLength(200);
    }
}
