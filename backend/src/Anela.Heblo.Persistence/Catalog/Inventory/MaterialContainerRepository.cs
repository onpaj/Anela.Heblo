using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerRepository : BaseRepository<MaterialContainer, int>, IMaterialContainerRepository
{
    public MaterialContainerRepository(ApplicationDbContext context) : base(context) { }

    public Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct)
        => DbSet.FirstOrDefaultAsync(x => x.Code == code, ct);

    public async Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        string? materialCode, string? lotCode, int page, int pageSize, CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(x => x.MaterialCode == materialCode);

        if (!string.IsNullOrWhiteSpace(lotCode))
            query = query.Where(x => x.LotCode == lotCode);

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

    public async Task<string?> GetLastUsedLotCodeForMaterialAsync(string materialCode, CancellationToken ct)
    {
        return await DbSet
            .Where(x => x.MaterialCode == materialCode)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.LotCode)
            .FirstOrDefaultAsync(ct);
    }
}
