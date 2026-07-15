using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotCalibrationLabel;

public class PrintLotCalibrationLabelHandler
    : IRequestHandler<PrintLotCalibrationLabelRequest, PrintLotCalibrationLabelResponse>
{
    private readonly ILogger<PrintLotCalibrationLabelHandler> _logger;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly ILotLabelCalibrationRepository _calibrationRepository;
    private readonly IPrinterMediaStateRepository _mediaStateRepository;
    private readonly ICurrentUserService _currentUserService;

    public PrintLotCalibrationLabelHandler(
        ILogger<PrintLotCalibrationLabelHandler> logger,
        ILabelPrintingService labelPrinter,
        ILotLabelCalibrationRepository calibrationRepository,
        IPrinterMediaStateRepository mediaStateRepository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _labelPrinter = labelPrinter;
        _calibrationRepository = calibrationRepository;
        _mediaStateRepository = mediaStateRepository;
        _currentUserService = currentUserService;
    }

    public async Task<PrintLotCalibrationLabelResponse> Handle(
        PrintLotCalibrationLabelRequest request, CancellationToken cancellationToken)
    {
        var mediaState = await _mediaStateRepository.GetAsync(cancellationToken);
        if (mediaState.RequiresConfirmation(LabelMediaType.LotRound) && !request.MediaChangeConfirmed)
        {
            return new PrintLotCalibrationLabelResponse { RequiresMediaChangeConfirmation = true };
        }

        var calibration = await _calibrationRepository.GetAsync(cancellationToken);
        var zpl = LotLabelZplBuilder.BuildCalibrationCross(calibration.PitchDots);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        mediaState.RecordPrint(LabelMediaType.LotRound, _currentUserService.GetCurrentUser().Name ?? "System");
        await _mediaStateRepository.SaveAsync(mediaState, cancellationToken);

        _logger.LogInformation("Printed lot label calibration cross with pitch {PitchDots} dots", calibration.PitchDots);

        return new PrintLotCalibrationLabelResponse();
    }
}
