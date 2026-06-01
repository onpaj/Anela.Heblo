namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IEshopStockClient
{
    Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken);
    Task UpdateStockAsync(string productCode, double amountChange, CancellationToken ct = default);
    Task<EshopStockSupply?> GetSupplyAsync(string productCode, CancellationToken ct = default);
    Task SetRealStockAsync(string productCode, double realStock, CancellationToken ct = default);
}