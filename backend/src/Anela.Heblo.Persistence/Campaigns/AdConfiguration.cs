using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdConfiguration : IEntityTypeConfiguration<Ad>
{
    public void Configure(EntityTypeBuilder<Ad> builder)
    {
        builder.ToTable("Ads", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PlatformAdId)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(e => e.PlatformAdId)
            .IsUnique();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Status)
            .HasMaxLength(50);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone");

        builder.HasMany(e => e.DailyMetrics)
            .WithOne(e => e.Ad)
            .HasForeignKey(e => e.AdId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
