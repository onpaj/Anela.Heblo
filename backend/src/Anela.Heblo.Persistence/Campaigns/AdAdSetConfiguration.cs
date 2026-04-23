using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdAdSetConfiguration : IEntityTypeConfiguration<AdAdSet>
{
    public void Configure(EntityTypeBuilder<AdAdSet> builder)
    {
        builder.ToTable("AdAdSets", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PlatformAdSetId)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(e => e.PlatformAdSetId)
            .IsUnique();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Status)
            .HasMaxLength(50);

        builder.Property(e => e.DailyBudget)
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone");

        builder.HasMany(e => e.Ads)
            .WithOne(e => e.AdSet)
            .HasForeignKey(e => e.AdSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
