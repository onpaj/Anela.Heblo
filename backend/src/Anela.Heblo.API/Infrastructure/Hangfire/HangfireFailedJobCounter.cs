using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Hangfire;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Hangfire-backed implementation of <see cref="IFailedJobCounter"/>.
/// Queries the Hangfire monitoring API for the current failed-job count.
/// </summary>
public sealed class HangfireFailedJobCounter : IFailedJobCounter
{
    private readonly JobStorage _jobStorage;

    public HangfireFailedJobCounter(JobStorage jobStorage)
    {
        _jobStorage = jobStorage ?? throw new ArgumentNullException(nameof(jobStorage));
    }

    public Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default)
    {
        // Hangfire's FailedCount() is synchronous; token accepted for interface conformance.
        var count = _jobStorage.GetMonitoringApi().FailedCount();
        return Task.FromResult(count);
    }
}
