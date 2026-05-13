using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdCampaignConfiguration : IEntityTypeConfiguration<AdCampaign>
{
    public void Configure(EntityTypeBuilder<AdCampaign> builder)
    {
        builder.ToTable("AdCampaigns", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Platform)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.PlatformCampaignId)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(e => new { e.Platform, e.PlatformCampaignId })
            .IsUnique();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Status)
            .HasMaxLength(50);

        builder.Property(e => e.Objective)
            .HasMaxLength(100);

        builder.Property(e => e.DailyBudget)
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.LifetimeBudget)
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.StartDate)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.EndDate)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone");

        builder.HasMany(e => e.AdSets)
            .WithOne(e => e.Campaign)
            .HasForeignKey(e => e.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
