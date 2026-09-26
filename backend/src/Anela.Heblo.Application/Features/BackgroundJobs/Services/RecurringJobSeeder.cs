using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public class RecurringJobSeeder : IRecurringJobSeeder
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public RecurringJobSeeder(IRecurringJobConfigurationRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
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
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Create configurations from discovered job metadata
        var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
            job.Metadata.JobName,
            job.Metadata.DisplayName,
            job.Metadata.Description,
            job.Metadata.CronExpression,
            job.Metadata.TimeZoneId,
            job.Metadata.DefaultIsEnabled,
            "System",
            now
        )).ToArray();

        foreach (var config in defaultConfigurations)
        {
            var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _repository.AddAsync(config, cancellationToken);
            }
            else if (HasSeededFieldsChanged(existing, config))
            {
                existing.UpdateConfiguration(
                    config.DisplayName,
                    config.Description,
                    existing.CronExpression,   // preserve admin override
                    config.TimeZoneId,
                    "System",
                    now);
                await _repository.UpdateAsync(existing, cancellationToken);
            }
            // else: no developer-owned field changed - leave the row (including
            // LastModifiedAt/LastModifiedBy, which record admin actions) untouched.
        }
    }

    /// <summary>
    /// Compares only the developer-owned, code-sourced fields (DisplayName, Description,
    /// TimeZoneId) between the stored row and the freshly computed metadata. CronExpression
    /// and IsEnabled are intentionally excluded - they are admin-owned and must never trigger
    /// a seeder-initiated write.
    /// </summary>
    private static bool HasSeededFieldsChanged(RecurringJobConfiguration existing, RecurringJobConfiguration config) =>
        existing.DisplayName != config.DisplayName
        || existing.Description != config.Description
        || existing.TimeZoneId != config.TimeZoneId;
}
