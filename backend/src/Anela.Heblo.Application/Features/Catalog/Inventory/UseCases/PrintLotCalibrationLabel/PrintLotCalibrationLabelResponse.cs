using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotCalibrationLabel;

public class PrintLotCalibrationLabelResponse : BaseResponse
{
    public PrintLotCalibrationLabelResponse() : base() { }

    public PrintLotCalibrationLabelResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
