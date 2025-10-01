namespace Anela.Heblo.Domain.Features.Catalog.Products;

public interface IProductWeightClient
{
    Task<double?> RefreshProductWeight(string productCode, CancellationToken cancellationToken = default);
}