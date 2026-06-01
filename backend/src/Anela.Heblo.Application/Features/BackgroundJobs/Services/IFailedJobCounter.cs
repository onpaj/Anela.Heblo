namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Abstraction over the background-job system that returns the current number of failed jobs.
/// Implemented by an infrastructure adapter (e.g. Hangfire) registered in the API project.
/// </summary>
public interface IFailedJobCounter
{
    /// <summary>
    /// Returns the current count of failed background jobs.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Number of failed jobs.</returns>
    Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default);
}
