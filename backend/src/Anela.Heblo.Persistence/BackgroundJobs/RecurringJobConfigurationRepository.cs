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

    public async Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await _context.RecurringJobConfigurations.AddAsync(configuration, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _context.RecurringJobConfigurations.Update(configuration);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
