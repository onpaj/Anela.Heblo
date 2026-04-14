namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IEshopStockClient
{
    Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken);
    Task UpdateStockAsync(string productCode, double amountChange, CancellationToken ct = default);
}