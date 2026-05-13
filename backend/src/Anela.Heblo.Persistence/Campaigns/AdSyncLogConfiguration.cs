using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdSyncLogConfiguration : IEntityTypeConfiguration<AdSyncLog>
{
    public void Configure(EntityTypeBuilder<AdSyncLog> builder)
    {
        builder.ToTable("AdSyncLogs", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Platform)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.StartedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.CompletedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2000);

        builder.HasIndex(e => new { e.Platform, e.StartedAt });
    }
}
