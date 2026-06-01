using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class MeetingTranscriptConfiguration : IEntityTypeConfiguration<MeetingTranscript>
{
    public void Configure(EntityTypeBuilder<MeetingTranscript> builder)
    {
        builder.ToTable("MeetingTranscripts", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlaudRecordingId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.PlaudCreatedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.Subject)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Summary)
            .IsRequired();

        builder.Property(x => x.RawTranscript)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(x => x.ReceivedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.ReviewedAt)
            .IsRequired(false)
            .AsUtcTimestamp();

        builder.Property(x => x.ReviewedByUser)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.HasMany(x => x.Tasks)
            .WithOne(x => x.MeetingTranscript)
            .HasForeignKey(x => x.MeetingTranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.AccessLevel)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(MeetingAccessLevel.Private);

        builder.HasMany(x => x.AccessGrants)
            .WithOne(x => x.MeetingTranscript)
            .HasForeignKey(x => x.MeetingTranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.AccessLevel)
            .HasDatabaseName("IX_MeetingTranscripts_AccessLevel");

        builder.HasIndex(x => x.PlaudRecordingId)
            .IsUnique()
            .HasDatabaseName("UX_MeetingTranscripts_PlaudRecordingId");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_MeetingTranscripts_Status");

        builder.HasIndex(x => x.ReceivedAt)
            .HasDatabaseName("IX_MeetingTranscripts_ReceivedAt");
    }
}
