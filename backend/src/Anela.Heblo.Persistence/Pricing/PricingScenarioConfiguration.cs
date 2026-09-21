using Anela.Heblo.Domain.Features.Pricing;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Pricing;

public class PricingScenarioConfiguration : IEntityTypeConfiguration<PricingScenario>
{
    public void Configure(EntityTypeBuilder<PricingScenario> builder)
    {
        builder.ToTable("PricingScenarios", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(320);
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ModifiedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.FilterJson).IsRequired().HasColumnType("jsonb");

        builder.HasIndex(x => x.Name).IsUnique();

        builder.HasMany(x => x.Items)
            .WithOne(x => x.Scenario!)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
