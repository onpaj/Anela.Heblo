using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Logistics;

public interface ICarrierCoolingRepository
{
    Task<IReadOnlyList<CarrierCoolingSetting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(Carriers carrier, DeliveryHandling deliveryHandling, Cooling cooling, string modifiedBy, CancellationToken cancellationToken = default);
}
