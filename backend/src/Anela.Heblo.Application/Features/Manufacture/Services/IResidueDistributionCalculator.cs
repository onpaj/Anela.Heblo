using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IResidueDistributionCalculator
{
    Task<ResidueDistribution> CalculateAsync(UpdateManufactureOrderDto order, CancellationToken cancellationToken = default);
}
