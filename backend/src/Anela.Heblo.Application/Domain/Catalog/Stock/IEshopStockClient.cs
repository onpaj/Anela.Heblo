namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public interface IEshopStockClient
{
    Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken);
}