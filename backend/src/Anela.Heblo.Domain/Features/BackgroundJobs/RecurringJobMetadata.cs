namespace Anela.Heblo.Domain.Features.BackgroundJobs;

/// <summary>
/// Metadata describing a recurring job for registration and configuration
/// </summary>
public class RecurringJobMetadata
{
    /// <summary>
    /// Unique job identifier (kebab-case, e.g., "product-weight-recalculation")
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Human-readable display name
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Detailed description of what the job does
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Cron expression for scheduling (e.g., "0 2 * * *")
    /// </summary>
    public required string CronExpression { get; init; }

    /// <summary>
    /// Whether job should be enabled by default
    /// </summary>
    public bool DefaultIsEnabled { get; init; } = true;

    /// <summary>
    /// Queue name for job execution
    /// </summary>
    public string QueueName { get; init; } = "heblo";

    /// <summary>
    /// Timezone for cron expression (defaults to Europe/Prague)
    /// </summary>
    public string TimeZoneId { get; init; } = "Europe/Prague";
}
