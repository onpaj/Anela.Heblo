using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotCalibrationLabel;

public class PrintLotCalibrationLabelResponse : BaseResponse
{
    /// <summary>True when the print was blocked because the media type changed; the client must confirm and resend.</summary>
    public bool RequiresMediaChangeConfirmation { get; set; }

    public PrintLotCalibrationLabelResponse() : base() { }

    public PrintLotCalibrationLabelResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
