namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public interface ICatalogTransportSource
{
    Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken);
}
