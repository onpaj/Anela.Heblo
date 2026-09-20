using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.NudgeLotLabelCalibration;

public class NudgeLotLabelCalibrationResponse : BaseResponse
{
    /// <summary>
    /// True when the calibration was already at the end of its adjustable range in the
    /// reported direction and therefore did not move. Without it the wizard would confirm
    /// an adjustment that never happened and invite the operator to keep clicking.
    /// </summary>
    public bool IsAtLimit { get; set; }

    public NudgeLotLabelCalibrationResponse() : base() { }

    public NudgeLotLabelCalibrationResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
