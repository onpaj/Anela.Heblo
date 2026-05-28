using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerRepository : BaseRepository<MaterialContainer, int>, IMaterialContainerRepository
{
    public MaterialContainerRepository(ApplicationDbContext context) : base(context) { }

    public async Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.Code == code, ct);
    }

    public async Task<bool> AnyByLotIdAsync(int lotId, CancellationToken ct)
    {
        return await DbSet.AnyAsync(x => x.LotId == lotId, ct);
    }

    public async Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        int? lotId,
        string? materialCode,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (lotId.HasValue)
            query = query.Where(x => x.LotId == lotId.Value);

        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            var lotIds = await Context.Set<Lot>()
                .Where(l => l.MaterialCode == materialCode)
                .Select(l => l.Id)
                .ToListAsync(ct);
            query = query.Where(x => lotIds.Contains(x.LotId));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<MaterialContainer>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }
}
