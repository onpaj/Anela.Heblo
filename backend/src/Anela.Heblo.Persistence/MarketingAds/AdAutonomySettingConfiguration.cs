using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdAutonomySettingConfiguration : IEntityTypeConfiguration<AdAutonomySetting>
{
    public void Configure(EntityTypeBuilder<AdAutonomySetting> builder)
    {
        builder.ToTable("AdAutonomySettings", "public");
        builder.HasKey(x => new { x.Platform, x.ActionType });
        builder.Property(x => x.Platform).HasMaxLength(32);
        builder.Property(x => x.ActionType).HasMaxLength(64);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedBy).HasMaxLength(200).IsRequired();
        builder.HasData(MarketingAdsSeedData.Autonomy());
    }
}
