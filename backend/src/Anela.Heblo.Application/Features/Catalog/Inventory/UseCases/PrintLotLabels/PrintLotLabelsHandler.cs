using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotLabels;

public class PrintLotLabelsHandler
    : IRequestHandler<PrintLotLabelsRequest, PrintLotLabelsResponse>
{
    private readonly ILogger<PrintLotLabelsHandler> _logger;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly ILotLabelCalibrationRepository _calibrationRepository;

    public PrintLotLabelsHandler(
        ILogger<PrintLotLabelsHandler> logger,
        ILabelPrintingService labelPrinter,
        ILotLabelCalibrationRepository calibrationRepository)
    {
        _logger = logger;
        _labelPrinter = labelPrinter;
        _calibrationRepository = calibrationRepository;
    }

    public async Task<PrintLotLabelsResponse> Handle(
        PrintLotLabelsRequest request, CancellationToken cancellationToken)
    {
        var calibration = await _calibrationRepository.GetAsync(cancellationToken);
        var zpl = LotLabelZplBuilder.Build(request.LotNumber, request.Expiration, request.Count, calibration.PitchDots);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

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
