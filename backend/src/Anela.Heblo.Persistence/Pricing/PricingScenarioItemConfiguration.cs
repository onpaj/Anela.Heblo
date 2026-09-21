using Anela.Heblo.Domain.Features.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Pricing;

public class PricingScenarioItemConfiguration : IEntityTypeConfiguration<PricingScenarioItem>
{
    public void Configure(EntityTypeBuilder<PricingScenarioItem> builder)
    {
        builder.ToTable("PricingScenarioItems", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProductCode).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Price).HasPrecision(18, 4);
        builder.Property(x => x.MaterialCost).HasPrecision(18, 4);
        builder.Property(x => x.ManufacturingCost).HasPrecision(18, 4);
        builder.Property(x => x.BaselinePrice).HasPrecision(18, 4);
        builder.Property(x => x.BaselineMaterialCost).HasPrecision(18, 4);
        builder.Property(x => x.BaselineManufacturingCost).HasPrecision(18, 4);

        builder.HasIndex(x => new { x.ScenarioId, x.ProductCode }).IsUnique();
    }
}
