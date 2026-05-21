using Anela.Heblo.Domain.Features.FeatureFlags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.FeatureFlags;

public sealed class FeatureFlagOverrideConfiguration : IEntityTypeConfiguration<FeatureFlagOverride>
{
    public void Configure(EntityTypeBuilder<FeatureFlagOverride> builder)
    {
        builder.ToTable("FeatureFlagOverrides", "public");
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasMaxLength(100);
        builder.Property(e => e.UpdatedBy).HasMaxLength(200);
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
    }
}
