namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public interface IEshopStockDomainService
{
    Task StockUpAsync(StockUpRequest stockUpOrder);

    Task<StockTakingRecord> SubmitStockTakingAsync(EshopStockTakingRequest order);
}