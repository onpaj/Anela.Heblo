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

    public async Task<bool> IsJobEnabledAsync(string jobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = await _repository.GetByJobNameAsync(jobName, cancellationToken);

            if (configuration == null)
            {
                // Safety fallback - if configuration doesn't exist, allow job to run
                _logger.LogWarning("Job configuration not found for job: {JobName}. Allowing job to run by default.", jobName);
                return true;
            }

            if (!configuration.IsEnabled)
            {
                _logger.LogInformation("Job {JobName} is disabled. Job will be skipped.", jobName);
            }

            return configuration.IsEnabled;
        }
        catch (Exception ex)
        {
            // Safety fallback - on error, allow job to run to avoid blocking critical jobs
            _logger.LogError(ex, "Error checking job enabled status for job: {JobName}. Allowing job to run by default.", jobName);
            return true;
        }
    }
}
