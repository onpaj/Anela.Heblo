namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
