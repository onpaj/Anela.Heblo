using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.CarrierCooling.Infrastructure;

internal sealed class CarrierCoolingPackingCarrierCoolingAdapter : IPackingCarrierCoolingSource
{
    private readonly ICarrierCoolingRepository _repository;

    public CarrierCoolingPackingCarrierCoolingAdapter(ICarrierCoolingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(CancellationToken ct = default)
    {
        var settings = await _repository.GetAllAsync(ct);
        return settings.Select(s => new PackingCarrierCoolingSetting
        {
            CarrierName = s.Carrier.ToString(),
            DeliveryHandlingName = s.DeliveryHandling.ToString(),
            Cooling = s.Cooling,
        }).ToList();
    }
}
