using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.SetLotLabelCalibration;

public class SetLotLabelCalibrationResponse : BaseResponse
{
    public int PitchDots { get; set; }
    public int DriftEveryNLabels { get; set; }

    public SetLotLabelCalibrationResponse() : base() { }

    public SetLotLabelCalibrationResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
