namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobStatusChecker
{
    Task<bool> IsJobEnabledAsync(string jobName, CancellationToken cancellationToken = default);
}
