using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IResidueDistributionCalculator
{
    Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default);
}
