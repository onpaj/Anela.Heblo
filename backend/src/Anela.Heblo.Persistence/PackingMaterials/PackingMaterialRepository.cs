using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.PackingMaterials;

public class PackingMaterialRepository : BaseRepository<PackingMaterial, int>, IPackingMaterialRepository
{
    public PackingMaterialRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialLog>()
            .Where(log => log.PackingMaterialId == packingMaterialId && log.CreatedAt >= fromDate)
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default)
    {
        var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
        if (ids.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<PackingMaterialLog>>();
        }

        var logs = await Context.Set<PackingMaterialLog>()
            .Where(log => ids.Contains(log.PackingMaterialId) && log.CreatedAt >= fromDate)
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);

        return logs
            .GroupBy(log => log.PackingMaterialId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PackingMaterialLog>)g.ToList());
    }

    public async Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await Context.Set<PackingMaterialLog>()
            .AnyAsync(log => log.Date == date && log.LogType == LogEntryType.AutomaticConsumption, cancellationToken);
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