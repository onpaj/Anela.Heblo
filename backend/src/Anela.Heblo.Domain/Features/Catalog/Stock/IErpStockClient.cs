namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IErpStockClient
{
    Task<IReadOnlyList<ErpStock>> ListAsync(CancellationToken cancellationToken);
}