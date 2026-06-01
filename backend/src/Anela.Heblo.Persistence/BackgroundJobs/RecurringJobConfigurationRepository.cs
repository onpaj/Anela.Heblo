using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.BackgroundJobs;

public class RecurringJobConfigurationRepository : IRecurringJobConfigurationRepository
{
    private readonly ApplicationDbContext _context;

    public RecurringJobConfigurationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .OrderBy(c => c.JobName)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .FirstOrDefaultAsync(c => c.JobName == jobName, cancellationToken);
    }

    public async Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _context.RecurringJobConfigurations.Update(configuration);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds database with configurations from discovered IRecurringJob implementations.
    /// Only creates configurations for jobs that don't already exist in the database.
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
            job.Metadata.DefaultIsEnabled,
            "System"
        )).ToArray();

        foreach (var config in defaultConfigurations)
        {
            var existing = await GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
