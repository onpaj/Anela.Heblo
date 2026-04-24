using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.DataQuality;

public class DqtRunConfiguration : IEntityTypeConfiguration<DqtRun>
{
    public void Configure(EntityTypeBuilder<DqtRun> builder)
    {
        builder.ToTable("DqtRuns", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired();

        builder.Property(e => e.TestType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.DateFrom)
            .IsRequired();

        builder.Property(e => e.DateTo)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.StartedAt)
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(e => e.CompletedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.TriggerType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.TotalChecked)
            .IsRequired();

        builder.Property(e => e.TotalMismatches)
            .IsRequired();

        builder.Property(e => e.ErrorMessage);

        builder.HasMany(e => e.Results)
            .WithOne()
            .HasForeignKey(e => e.DqtRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TestType, e.StartedAt })
            .HasDatabaseName("IX_DqtRuns_TestType_StartedAt");
    }
}
