using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public class RecurringJobSeeder : IRecurringJobSeeder
{
    private readonly IRecurringJobConfigurationRepository _repository;

    public RecurringJobSeeder(IRecurringJobConfigurationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Seeds database with configurations from discovered IRecurringJob implementations.
    /// Creates configurations for jobs that don't already exist in the database. For jobs
    /// that already have a configuration row, updates the developer-owned fields
    /// (DisplayName, Description) to match the current code, while preserving the
    /// admin-owned fields (CronExpression, IsEnabled) exactly as stored.
    /// </summary>
    /// <param name="jobs">Collection of discovered recurring jobs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
    {
        // Create configurations from discovered job metadata
        var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
            job.Metadata.JobName,
            job.Metadata.DisplayName,
            job.Metadata.Description,
            job.Metadata.CronExpression,
            job.Metadata.TimeZoneId,
            job.Metadata.DefaultIsEnabled,
            "System"
        )).ToArray();

        foreach (var config in defaultConfigurations)
        {
            var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _repository.AddAsync(config, cancellationToken);
            }
            else
            {
                existing.UpdateConfiguration(
                    config.DisplayName,
                    config.Description,
                    existing.CronExpression,   // preserve admin override
                    "System");
                await _repository.UpdateAsync(existing, cancellationToken);
            }
        }
    }
}
