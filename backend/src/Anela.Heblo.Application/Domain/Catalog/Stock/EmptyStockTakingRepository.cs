using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public class EmptyStockTakingRepository : EmptyRepository<StockTakingRecord, int>, IStockTakingRepository
{
}