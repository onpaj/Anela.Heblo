using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class ProposedTaskConfiguration : IEntityTypeConfiguration<ProposedTask>
{
    public void Configure(EntityTypeBuilder<ProposedTask> builder)
    {
        builder.ToTable("ProposedTasks", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Description)
            .IsRequired();

        builder.Property(x => x.Assignee)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.AssigneeEmail)
            .HasMaxLength(320)
            .IsRequired(false);

        builder.Property(x => x.DueDate)
            .IsRequired(false)
            .AsUtcTimestamp();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(x => x.ExternalTaskId)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(x => x.IsManuallyAdded)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(x => x.MeetingTranscriptId)
            .HasDatabaseName("IX_ProposedTasks_MeetingTranscriptId");
    }
}
