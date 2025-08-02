namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IErpStockDomainService
{
    Task<StockTakingRecord> SubmitStockTakingAsync(ErpStockTakingRequest order);
}