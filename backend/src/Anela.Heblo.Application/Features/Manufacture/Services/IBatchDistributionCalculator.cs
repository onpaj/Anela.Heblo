namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IBatchDistributionCalculator
{
    void OptimizeBatch(ProductBatch batch, bool minimizeResidue = true);
}