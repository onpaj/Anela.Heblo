using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdDailyMetricConfiguration : IEntityTypeConfiguration<AdDailyMetric>
{
    public void Configure(EntityTypeBuilder<AdDailyMetric> builder)
    {
        builder.ToTable("AdDailyMetrics", "dbo");

        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.AdId, e.Date })
            .IsUnique();

        builder.Property(e => e.Date)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.Spend)
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.Revenue)
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone");

        // Computed properties — not mapped to columns
        builder.Ignore(e => e.Ctr);
        builder.Ignore(e => e.Cpc);
        builder.Ignore(e => e.Roas);
    }
}
