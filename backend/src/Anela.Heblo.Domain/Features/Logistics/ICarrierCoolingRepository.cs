namespace Anela.Heblo.Domain.Features.Logistics;

public interface ICarrierCoolingRepository
{
    Task<IReadOnlyList<CarrierCoolingSetting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(CarrierCoolingSetting setting, CancellationToken cancellationToken = default);
}
