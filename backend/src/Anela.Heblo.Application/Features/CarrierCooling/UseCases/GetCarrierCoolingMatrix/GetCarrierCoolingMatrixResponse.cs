using Anela.Heblo.Application.Features.CarrierCooling.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;

public class GetCarrierCoolingMatrixResponse : BaseResponse
{
    public List<CarrierGroupDto> Groups { get; set; } = new();
}
