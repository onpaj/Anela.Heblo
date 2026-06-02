using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Logistics;

public class CarrierCoolingSetting
{
    public Carriers Carrier { get; private set; }
    public DeliveryHandling DeliveryHandling { get; private set; }
    public Cooling Cooling { get; private set; }
    public string? CoolingText { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    public string ModifiedBy { get; private set; } = null!;

    private CarrierCoolingSetting() { }

    public CarrierCoolingSetting(Carriers carrier, DeliveryHandling deliveryHandling, Cooling cooling, string modifiedBy, string? coolingText = null)
    {
        Carrier = carrier;
        DeliveryHandling = deliveryHandling;
        Cooling = cooling;
        CoolingText = coolingText;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }

    internal void UpdateCooling(Cooling cooling, string modifiedBy, string? coolingText = null)
    {
        Cooling = cooling;
        CoolingText = coolingText;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }
}
