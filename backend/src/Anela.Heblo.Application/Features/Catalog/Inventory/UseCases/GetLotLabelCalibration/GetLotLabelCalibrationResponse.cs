using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLotLabelCalibration;

public class GetLotLabelCalibrationResponse : BaseResponse
{
    public int PitchDots { get; set; }
    public int MinPitchDots { get; set; }
    public int MaxPitchDots { get; set; }
    public int DriftEveryNLabels { get; set; }
    public int MinDriftEveryNLabels { get; set; }
    public int MaxDriftEveryNLabels { get; set; }

    public GetLotLabelCalibrationResponse() : base() { }

    public GetLotLabelCalibrationResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
