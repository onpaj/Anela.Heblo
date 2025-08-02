using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class EmptyStockTakingRepository : EmptyRepository<StockTakingRecord, int>, IStockTakingRepository
{
}