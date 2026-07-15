using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.SetLotLabelCalibration;

public class SetLotLabelCalibrationRequest : IRequest<SetLotLabelCalibrationResponse>
{
    public int PitchDots { get; set; }
    public int DriftDotsPer100Labels { get; set; }
}
