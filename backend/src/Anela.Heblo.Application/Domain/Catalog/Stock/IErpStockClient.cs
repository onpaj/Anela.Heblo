namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public interface IErpStockClient 
{
    Task<IReadOnlyList<ErpStock>> ListAsync(CancellationToken cancellationToken);
}