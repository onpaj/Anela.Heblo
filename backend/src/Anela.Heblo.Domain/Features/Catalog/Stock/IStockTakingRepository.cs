using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IStockTakingRepository : IRepository<StockTakingRecord, int>
{
    Task<List<StockTakingRecord>> GetByDateRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default);
}