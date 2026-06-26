using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.CarrierCooling.Contracts;

public class CarrierCoolingRowDto
{
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
    public string? CoolingText { get; set; }
}
