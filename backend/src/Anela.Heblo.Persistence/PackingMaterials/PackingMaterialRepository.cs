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
        return await Context.Set<PackingMaterialLog>()
            .AnyAsync(log => log.Date == date && log.LogType == LogEntryType.AutomaticConsumption, cancellationToken);
    }
}