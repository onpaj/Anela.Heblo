namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public interface IErpStockDomainService
{
    Task<StockTakingRecord> SubmitStockTakingAsync(ErpStockTakingRequest order);
}