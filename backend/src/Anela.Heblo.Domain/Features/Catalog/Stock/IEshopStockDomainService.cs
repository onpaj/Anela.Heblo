namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IEshopStockDomainService
{
    Task StockUpAsync(StockUpRequest stockUpOrder);

    Task<StockTakingRecord> SubmitStockTakingAsync(EshopStockTakingRequest order);
}