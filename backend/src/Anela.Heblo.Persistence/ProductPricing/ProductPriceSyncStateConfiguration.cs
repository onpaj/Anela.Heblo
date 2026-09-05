using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceSyncStateConfiguration : IEntityTypeConfiguration<ProductPriceSyncState>
{
    public void Configure(EntityTypeBuilder<ProductPriceSyncState> builder)
    {
        builder.ToTable("ProductPriceSyncStates", "public");

        builder.HasKey(e => new { e.ProductCode, e.Target });

        builder.Property(e => e.ProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Target)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.LastPushedPriceWithVat)
            .IsRequired(false)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.RemoteValueAtConflict)
            .IsRequired(false)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.LastPushedAt)
            .IsRequired(false)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ConflictDetectedAt)
            .IsRequired(false)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastError)
            .IsRequired(false)
            .HasMaxLength(2000);

        // The conflicts worklist queries by status across both targets.
        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_ProductPriceSyncStates_Status");
    }
}
