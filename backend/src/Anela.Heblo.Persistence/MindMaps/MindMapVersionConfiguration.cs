using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapVersionConfiguration : IEntityTypeConfiguration<MindMapVersion>
{
    public void Configure(EntityTypeBuilder<MindMapVersion> builder)
    {
        builder.ToTable("MindMapVersions", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Json).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.TriggerMeetingId).IsRequired(false);

        builder.HasIndex(x => new { x.MindMapId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("UX_MindMapVersions_MindMapId_VersionNumber");
    }
}
