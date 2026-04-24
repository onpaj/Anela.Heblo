using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.DataQuality;

public class DqtRunConfiguration : IEntityTypeConfiguration<DqtRun>
{
    public void Configure(EntityTypeBuilder<DqtRun> builder)
    {
        builder.ToTable("dqt_runs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(e => e.TestType)
            .HasColumnName("test_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.DateFrom)
            .HasColumnName("date_from")
            .IsRequired();

        builder.Property(e => e.DateTo)
            .HasColumnName("date_to")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(e => e.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.TriggerType)
            .HasColumnName("trigger_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.TotalChecked)
            .HasColumnName("total_checked")
            .IsRequired();

        builder.Property(e => e.TotalMismatches)
            .HasColumnName("total_mismatches")
            .IsRequired();

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message");

        builder.HasMany(e => e.Results)
            .WithOne()
            .HasForeignKey(e => e.DqtRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TestType, e.StartedAt })
            .HasDatabaseName("IX_dqt_runs_test_type_started_at");
    }
}
