using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MindMaps;

public class MindMapMeetingConfiguration : IEntityTypeConfiguration<MindMapMeeting>
{
    public void Configure(EntityTypeBuilder<MindMapMeeting> builder)
    {
        builder.ToTable("MindMapMeetings", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AttachedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ProcessedAt).IsRequired(false).AsUtcTimestamp();

        builder.HasOne(x => x.MeetingTranscript)
            .WithMany()
            .HasForeignKey(x => x.MeetingTranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.MindMapId, x.MeetingTranscriptId })
            .IsUnique()
            .HasDatabaseName("UX_MindMapMeetings_MindMapId_MeetingTranscriptId");
    }
}
