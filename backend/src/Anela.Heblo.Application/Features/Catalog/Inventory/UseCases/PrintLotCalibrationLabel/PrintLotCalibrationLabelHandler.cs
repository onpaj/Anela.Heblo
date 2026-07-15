using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotCalibrationLabel;

public class PrintLotCalibrationLabelHandler
    : IRequestHandler<PrintLotCalibrationLabelRequest, PrintLotCalibrationLabelResponse>
{
    private readonly ILogger<PrintLotCalibrationLabelHandler> _logger;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly ILotLabelCalibrationRepository _calibrationRepository;

    public PrintLotCalibrationLabelHandler(
        ILogger<PrintLotCalibrationLabelHandler> logger,
        ILabelPrintingService labelPrinter,
        ILotLabelCalibrationRepository calibrationRepository)
    {
        _logger = logger;
        _labelPrinter = labelPrinter;
        _calibrationRepository = calibrationRepository;
    }

    public async Task<PrintLotCalibrationLabelResponse> Handle(
        PrintLotCalibrationLabelRequest request, CancellationToken cancellationToken)
    {
        var calibration = await _calibrationRepository.GetAsync(cancellationToken);
        var zpl = LotLabelZplBuilder.BuildCalibrationCross(calibration.PitchDots);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        _logger.LogInformation("Printed lot label calibration cross with pitch {PitchDots} dots", calibration.PitchDots);

        return new PrintLotCalibrationLabelResponse();
    }
}
