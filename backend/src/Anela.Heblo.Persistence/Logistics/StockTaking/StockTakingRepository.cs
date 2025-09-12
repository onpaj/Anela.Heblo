using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.StockTaking;
using Anela.Heblo.Persistence.Repositories;

namespace Anela.Heblo.Persistence.Logistics.StockTaking;

public class StockTakingRepository : BaseRepository<StockTakingRecord, int>, IStockTakingRepository
{
    public StockTakingRepository(ApplicationDbContext context) : base(context)
    {
    }
}