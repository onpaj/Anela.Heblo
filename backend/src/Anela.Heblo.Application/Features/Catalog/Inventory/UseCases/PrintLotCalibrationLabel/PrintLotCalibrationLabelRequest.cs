using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotCalibrationLabel;

public class PrintLotCalibrationLabelRequest : IRequest<PrintLotCalibrationLabelResponse>
{
    /// <summary>Set by the client to confirm the operator has changed and calibrated the media when switching type.</summary>
    public bool MediaChangeConfirmed { get; set; }
}
