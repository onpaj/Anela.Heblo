using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.StockTaking;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Logistics.StockTaking;

public class StockTakingRepository : BaseRepository<StockTakingRecord, int>, IStockTakingRepository
{
    public StockTakingRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<StockTakingRecord>> GetByDateRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await Context.Set<StockTakingRecord>()
            .Where(r => r.Date >= from && r.Date <= to)
            .ToListAsync(ct);
    }
}