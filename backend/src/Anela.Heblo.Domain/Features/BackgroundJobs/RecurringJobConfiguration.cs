using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public class RecurringJobConfiguration : Entity<string>
{
    public string JobName { get; private set; }

    public string DisplayName { get; private set; }

    public string Description { get; private set; }

    public string CronExpression { get; private set; }

    public string TimeZoneId { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTime LastModifiedAt { get; private set; }

    public string LastModifiedBy { get; private set; }

    // Private constructor for EF Core
    private RecurringJobConfiguration()
    {
        JobName = string.Empty;
        DisplayName = string.Empty;
        Description = string.Empty;
        CronExpression = string.Empty;
        TimeZoneId = string.Empty;
        LastModifiedBy = string.Empty;
    }

    public RecurringJobConfiguration(
        string jobName,
        string displayName,
        string description,
        string cronExpression,
        string timeZoneId,
        bool isEnabled,
        string lastModifiedBy,
        DateTime lastModifiedAt)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            throw new ArgumentException("JobName is required");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required");
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression is required");
        ValidateCronFormat(cronExpression);
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ArgumentException("TimeZoneId is required");
        if (string.IsNullOrWhiteSpace(lastModifiedBy))
            throw new ArgumentException("LastModifiedBy is required");

        JobName = jobName;
        Id = jobName; // JobName is the primary key
        DisplayName = displayName;
        Description = description;
        CronExpression = cronExpression;
        TimeZoneId = timeZoneId;
        IsEnabled = isEnabled;
        LastModifiedAt = lastModifiedAt;
        LastModifiedBy = lastModifiedBy;
    }

    public void UpdateConfiguration(
        string displayName,
        string description,
        string cronExpression,
        string timeZoneId,
        string modifiedBy,
        DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required");
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression is required");
        ValidateCronFormat(cronExpression);
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ArgumentException("TimeZoneId is required");
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ArgumentException("ModifiedBy is required");

        DisplayName = displayName;
        Description = description;
        CronExpression = cronExpression;
        TimeZoneId = timeZoneId;
        LastModifiedAt = modifiedAt;
        LastModifiedBy = modifiedBy;
    }

    public void Enable(string modifiedBy, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ArgumentException("ModifiedBy is required");

        IsEnabled = true;
        LastModifiedAt = modifiedAt;
        LastModifiedBy = modifiedBy;
    }

    public void Disable(string modifiedBy, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ArgumentException("ModifiedBy is required");

        IsEnabled = false;
        LastModifiedAt = modifiedAt;
        LastModifiedBy = modifiedBy;
    }

    public void UpdateCronExpression(string cronExpression, string modifiedBy, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression is required");
        ValidateCronFormat(cronExpression);
        if (string.IsNullOrWhiteSpace(modifiedBy))
            throw new ArgumentException("ModifiedBy is required");

        CronExpression = cronExpression;
        LastModifiedAt = modifiedAt;
        LastModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Structural-only check: confirms the value has the field count of a standard
    /// (5-field) or Quartz-style (6-field, leading seconds) CRON expression. Does
    /// NOT validate per-field value ranges (e.g. "99 99 * * *" passes this check) —
    /// that stronger semantic validation is performed by
    /// UpdateRecurringJobCronHandler.IsValidCronExpression (NCrontab.Advanced) on
    /// the one user-facing write path. This check exists so the entity itself
    /// cannot be put into a state with an obviously malformed CronExpression via
    /// any caller, not only the MediatR handler.
    /// </summary>
    private static void ValidateCronFormat(string cronExpression)
    {
        var fields = cronExpression.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (5 or 6))
            throw new ArgumentException($"'{cronExpression}' is not a valid CRON expression.");
    }
}
