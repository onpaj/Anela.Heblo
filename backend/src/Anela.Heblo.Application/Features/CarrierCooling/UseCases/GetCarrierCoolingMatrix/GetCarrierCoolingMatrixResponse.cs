using Anela.Heblo.Application.Features.CarrierCooling.Contracts;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;

public class GetCarrierCoolingMatrixResponse
{
    public List<CarrierGroupDto> Groups { get; set; } = new();
}
