using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialRepository : BaseRepository<PackingMaterial, int>, IPackingMaterialRepository
{
    public PackingMaterialRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default)
    {
        // For now, return materials without logs since we removed the navigation property
        // We'll load logs separately when needed
        return await DbSet.ToListAsync(cancellationToken);
    }

    public async Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(pm => pm.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialLog>()
            .Where(log => log.PackingMaterialId == packingMaterialId && log.CreatedAt >= fromDate)
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialDailyRun>()
            .AnyAsync(r => r.Date == date, cancellationToken);
    }

    public async Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
    {
        await Context.Set<PackingMaterialDailyRun>().AddAsync(run, cancellationToken);
    }

    public async Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(pm => pm.Allocations)
            .ToListAsync(cancellationToken);
    }

    public async Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(pm => pm.Allocations)
            .FirstOrDefaultAsync(pm => pm.Id == id, cancellationToken);
    }

    public async Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default)
    {
        await Context.Set<PackingMaterialConsumption>().AddRangeAsync(rows, cancellationToken);
    }

    public async Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialConsumption>()
            .Where(c => c.Date == date)
            .ToListAsync(cancellationToken);
    }
}