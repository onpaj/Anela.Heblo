using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.NudgeLotLabelCalibration;

/// <summary>
/// An operator's observation of how the printed text drifts across the label face. The
/// server turns it into a calibration correction, so the caller never sends a dot value —
/// which is what lets this run on plain operator permissions.
/// </summary>
public class NudgeLotLabelCalibrationRequest : IRequest<NudgeLotLabelCalibrationResponse>
{
    public LabelDriftDirection Direction { get; set; }
    public LabelDriftSpeed Speed { get; set; }
}
