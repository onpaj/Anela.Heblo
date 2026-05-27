using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.CarrierCooling.Contracts;

public class CarrierGroupDto
{
    public Carriers Carrier { get; set; }
    public List<CarrierCoolingRowDto> Rows { get; set; } = new();
}
