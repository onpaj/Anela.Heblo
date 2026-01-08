using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public class RecurringJobConfiguration : Entity<string>
{
    [Required]
    [MaxLength(100)]
    public string JobName { get; private set; }

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; private set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; private set; }

    [Required]
    [MaxLength(50)]
    public string CronExpression { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTime LastModifiedAt { get; private set; }

    [Required]
    [MaxLength(100)]
    public string LastModifiedBy { get; private set; }

    // Private constructor for EF Core
    private RecurringJobConfiguration()
    {
        JobName = string.Empty;
        DisplayName = string.Empty;
        Description = string.Empty;
        CronExpression = string.Empty;
        LastModifiedBy = string.Empty;
    }

    public RecurringJobConfiguration(
        string jobName,
        string displayName,
        string description,
        string cronExpression,
        bool isEnabled,
        string lastModifiedBy)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            throw new ValidationException("JobName is required");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ValidationException("DisplayName is required");
        if (string.IsNullOrWhiteSpace(description))
            throw new ValidationException("Description is required");
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        if (string.IsNullOrWhiteSpace(lastModifiedBy))
            throw new ValidationException("LastModifiedBy is required");

        JobName = jobName;
        Id = jobName; // JobName is the primary key
        DisplayName = displayName;
        Description = description;
        CronExpression = cronExpression;
        IsEnabled = isEnabled;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = lastModifiedBy;
    }

    public void UpdateConfiguration(
        string displayName,
        string description,
        string cronExpression,
        string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ValidationException("DisplayName is required");
        if (string.IsNullOrWhiteSpace(description))
            throw new ValidationException("Description is required");
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ValidationException("CronExpression is required");
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ValidationException("ModifiedBy is required");

        DisplayName = displayName;
        Description = description;
        CronExpression = cronExpression;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
    }

    public void Enable(string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ValidationException("ModifiedBy is required");

        IsEnabled = true;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
    }

    public void Disable(string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ValidationException("ModifiedBy is required");

        IsEnabled = false;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
    }
}
