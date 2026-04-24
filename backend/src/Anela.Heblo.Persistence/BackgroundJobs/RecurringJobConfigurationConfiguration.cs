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
        builder.ToTable("RecurringJobConfigurations", "public");

        // Primary key is Id (inherited from Entity<string>), which is set to JobName
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.JobName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.CronExpression)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.IsEnabled)
            .IsRequired();

        builder.Property(e => e.LastModifiedAt)
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.Property(e => e.LastModifiedBy)
            .HasMaxLength(100)
            .IsRequired();

        // Create index on JobName for efficient lookups
        builder.HasIndex(e => e.JobName)
            .IsUnique()
            .HasDatabaseName("IX_RecurringJobConfigurations_JobName");

        // Create index on IsEnabled for filtering enabled/disabled jobs
        builder.HasIndex(e => e.IsEnabled)
            .HasDatabaseName("IX_RecurringJobConfigurations_IsEnabled");
    }
}
