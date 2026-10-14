using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MarketingAds;

public class AdKillSwitchConfiguration : IEntityTypeConfiguration<AdKillSwitch>
{
    public void Configure(EntityTypeBuilder<AdKillSwitch> builder)
    {
        builder.ToTable("AdKillSwitches", "public");
        builder.HasKey(x => x.Scope);
        builder.Property(x => x.Scope).HasMaxLength(32);
        builder.Property(x => x.UpdatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.UpdatedBy).HasMaxLength(200).IsRequired();
        builder.HasData(MarketingAdsSeedData.KillSwitches());
    }
}
