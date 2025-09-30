namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IProductWeightRecalculationService
{
    Task RecalculateAllProductWeights(CancellationToken cancellationToken = default);
}