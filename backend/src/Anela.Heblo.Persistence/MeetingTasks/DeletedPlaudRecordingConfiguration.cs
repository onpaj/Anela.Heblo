using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class DeletedPlaudRecordingConfiguration : IEntityTypeConfiguration<DeletedPlaudRecording>
{
    public void Configure(EntityTypeBuilder<DeletedPlaudRecording> builder)
    {
        builder.ToTable("DeletedPlaudRecordings", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlaudRecordingId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DeletedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.DeletedByUserEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.HasIndex(x => x.PlaudRecordingId)
            .IsUnique()
            .HasDatabaseName("UX_DeletedPlaudRecordings_PlaudRecordingId");
    }
}
