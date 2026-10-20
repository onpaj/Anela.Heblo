using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdLimitSettingConfiguration : IEntityTypeConfiguration<AdLimitSetting>
{
    public void Configure(EntityTypeBuilder<AdLimitSetting> builder)
    {
        builder.ToTable("AdLimitSettings", "public");
        builder.HasKey(x => new { x.Platform, x.ActionType, x.Key });
        builder.Property(x => x.Platform).HasMaxLength(32);
        builder.Property(x => x.ActionType).HasMaxLength(64);
        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedBy).HasMaxLength(200).IsRequired();
        builder.HasData(MarketingAdsSeedData.Limits());
    }
}
