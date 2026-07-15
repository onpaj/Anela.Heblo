using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotLabels;

public class PrintLotLabelsHandler
    : IRequestHandler<PrintLotLabelsRequest, PrintLotLabelsResponse>
{
    private readonly ILogger<PrintLotLabelsHandler> _logger;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly ILotLabelCalibrationRepository _calibrationRepository;
    private readonly IPrinterMediaStateRepository _mediaStateRepository;
    private readonly ICurrentUserService _currentUserService;

    public PrintLotLabelsHandler(
        ILogger<PrintLotLabelsHandler> logger,
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

    public async Task<PrintLotLabelsResponse> Handle(
        PrintLotLabelsRequest request, CancellationToken cancellationToken)
    {
        var mediaState = await _mediaStateRepository.GetAsync(cancellationToken);
        if (mediaState.RequiresConfirmation(LabelMediaType.LotRound) && !request.MediaChangeConfirmed)
        {
            return new PrintLotLabelsResponse { RequiresMediaChangeConfirmation = true };
        }

        var calibration = await _calibrationRepository.GetAsync(cancellationToken);
        var zpl = LotLabelZplBuilder.Build(
            request.LotNumber, request.Expiration, request.Count,
            calibration.PitchDots, calibration.DriftDotsPer100Labels);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        mediaState.RecordPrint(LabelMediaType.LotRound, _currentUserService.GetCurrentUser().Name ?? "System");
        await _mediaStateRepository.SaveAsync(mediaState, cancellationToken);

        _logger.LogInformation(
            "Printed {Count} lot labels (lot {LotNumber}, expiration {Expiration}, pitch {PitchDots} dots)",
            request.Count, request.LotNumber, request.Expiration, calibration.PitchDots);

        return new PrintLotLabelsResponse
        {
            LotNumber = request.LotNumber,
            Expiration = request.Expiration,
            Count = request.Count,
        };
    }
}
