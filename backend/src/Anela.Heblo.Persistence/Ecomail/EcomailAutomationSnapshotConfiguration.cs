using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailAutomationSnapshotConfiguration : IEntityTypeConfiguration<EcomailAutomationSnapshot>
{
    public void Configure(EntityTypeBuilder<EcomailAutomationSnapshot> builder)
    {
        builder.ToTable("EcomailAutomationSnapshots", "public");

        builder.HasKey(x => x.Id);

        // DateOnly maps to Postgres `date` — no AsUtcTimestamp needed, and no timezone hazard.
        builder.Property(x => x.CapturedOn).HasColumnType("date").IsRequired();

        builder.Property(x => x.ConversionsValue).HasPrecision(18, 2);

        // Makes the job safe to re-run inside a day: the second write is rejected, not duplicated.
        builder.HasIndex(x => new { x.PipelineId, x.CapturedOn }).IsUnique();
    }
}
