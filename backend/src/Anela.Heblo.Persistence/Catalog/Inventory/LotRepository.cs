using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class LotRepository : BaseRepository<Lot, int>, ILotRepository
{
    public LotRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Lot?> GetByIdWithEansAsync(int id, CancellationToken ct)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<PagedResult<Lot>> GetPaginatedAsync(
        string? materialCode,
        DateOnly? expirationFrom,
        DateOnly? expirationTo,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(x => x.MaterialCode == materialCode);

        if (expirationFrom.HasValue)
            query = query.Where(x => x.Expiration.HasValue && x.Expiration.Value >= expirationFrom.Value);

        if (expirationTo.HasValue)
            query = query.Where(x => x.Expiration.HasValue && x.Expiration.Value <= expirationTo.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.ReceivedDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Lot>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> ExistsAsync(string materialCode, string lotCode, CancellationToken ct)
    {
        return await DbSet.AnyAsync(x => x.MaterialCode == materialCode && x.LotCode == lotCode, ct);
    }
}
