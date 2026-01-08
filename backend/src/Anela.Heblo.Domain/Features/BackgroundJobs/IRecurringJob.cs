namespace Anela.Heblo.Domain.Features.BackgroundJobs;

/// <summary>
/// Marker interface for all recurring background jobs.
/// Implementations are automatically discovered via DI and registered with Hangfire.
/// </summary>
public interface IRecurringJob
{
    /// <summary>
    /// Job metadata for registration and database seeding
    /// </summary>
    RecurringJobMetadata Metadata { get; }

    /// <summary>
    /// Execute the job
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
