using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public class RecurringJobStatusChecker : IRecurringJobStatusChecker
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly ILogger<RecurringJobStatusChecker> _logger;

    public RecurringJobStatusChecker(
        IRecurringJobConfigurationRepository repository,
        ILogger<RecurringJobStatusChecker> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<bool> IsJobEnabledAsync(
        string jobName,
        CancellationToken cancellationToken = default,
        bool defaultIfMissing = true)
    {
        try
        {
            var configuration = await _repository.GetByJobNameAsync(jobName, cancellationToken);

            if (configuration == null)
            {
                // Return the caller-specified default when no configuration row exists
                if (defaultIfMissing)
                {
                    _logger.LogWarning("Job configuration not found for job: {JobName}. Allowing job to run by default.", jobName);
                }
                else
                {
                    _logger.LogWarning("Job configuration not found for job: {JobName}. Disabling job by caller request.", jobName);
                }
                return defaultIfMissing;
            }

            if (!configuration.IsEnabled)
            {
                _logger.LogInformation("Job {JobName} is disabled. Job will be skipped.", jobName);
            }

            return configuration.IsEnabled;
        }
        catch (Exception ex)
        {
            // Safety fallback - on error, use the caller-specified default to avoid blocking critical jobs
            _logger.LogError(ex, "Error checking job enabled status for job: {JobName}. Returning default: {DefaultIfMissing}.", jobName, defaultIfMissing);
            return defaultIfMissing;
        }
    }
}
