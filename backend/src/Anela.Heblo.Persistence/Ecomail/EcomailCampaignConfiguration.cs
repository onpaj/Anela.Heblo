using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailCampaignConfiguration : IEntityTypeConfiguration<EcomailCampaign>
{
    public void Configure(EntityTypeBuilder<EcomailCampaign> builder)
    {
        builder.ToTable("EcomailCampaigns", "public");

        builder.HasKey(x => x.Id);
        // Ecomail's id is the natural key — never generate one.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FromEmail).HasMaxLength(320).IsRequired(false);
        builder.Property(x => x.CampaignType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).IsRequired();

        builder.Property(x => x.SentAt).IsRequired(false).AsUtcTimestamp();
        builder.Property(x => x.SyncedAt).IsRequired().AsUtcTimestamp();

        builder.Property(x => x.ConversionsValue).HasPrecision(18, 2);

        // The reporting read path filters on these two together.
        builder.HasIndex(x => new { x.Status, x.CampaignType });
        builder.HasIndex(x => x.SentAt);
        builder.HasIndex(x => x.ParentId);

        builder.Ignore(x => x.IsReportable);
    }
}
