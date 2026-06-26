using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingCarrierCoolingSource
{
    Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(CancellationToken ct = default);
}

public class PackingCarrierCoolingSetting
{
    public string CarrierName { get; init; } = string.Empty;
    public string DeliveryHandlingName { get; init; } = string.Empty;
    public Cooling Cooling { get; init; }
}
