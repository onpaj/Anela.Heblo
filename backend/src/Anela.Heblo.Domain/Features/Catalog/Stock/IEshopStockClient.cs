namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IEshopStockClient
{
    Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken);
}