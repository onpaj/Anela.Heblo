using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.CarrierCooling.Contracts;

public class CarrierCoolingRowDto
{
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
}
