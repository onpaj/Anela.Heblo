using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.BackgroundJobs;

/// <summary>
/// EF Core configuration for RecurringJobConfiguration entity
/// </summary>
public class RecurringJobConfigurationConfiguration : IEntityTypeConfiguration<RecurringJobConfiguration>
{
    public void Configure(EntityTypeBuilder<RecurringJobConfiguration> builder)
    {
        builder.ToTable("recurring_job_configurations");

        // Primary key is Id (inherited from Entity<string>), which is set to JobName
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.JobName)
            .HasColumnName("job_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.CronExpression)
            .HasColumnName("cron_expression")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.IsEnabled)
            .HasColumnName("is_enabled")
            .IsRequired();

        builder.Property(e => e.LastModifiedAt)
            .HasColumnName("last_modified_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(e => e.LastModifiedBy)
            .HasColumnName("last_modified_by")
            .HasMaxLength(100)
            .IsRequired();

        // Create index on JobName for efficient lookups
        builder.HasIndex(e => e.JobName)
            .IsUnique()
            .HasDatabaseName("IX_recurring_job_configurations_job_name");

        // Create index on IsEnabled for filtering enabled/disabled jobs
        builder.HasIndex(e => e.IsEnabled)
            .HasDatabaseName("IX_recurring_job_configurations_is_enabled");
    }
}
