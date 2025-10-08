namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IProductWeightRecalculationService
{
    Task<ProductWeightRecalculationResult> RecalculateAllProductWeights(CancellationToken cancellationToken = default);
    Task<ProductWeightRecalculationResult> RecalculateProductWeight(string productCode, CancellationToken cancellationToken = default);
}