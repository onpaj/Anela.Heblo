using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;

public class FeedLotMediaHandler
    : IRequestHandler<FeedLotMediaRequest, FeedLotMediaResponse>
{
    private readonly ILogger<FeedLotMediaHandler> _logger;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly IPrinterMediaStateRepository _mediaStateRepository;
    private readonly ICurrentUserService _currentUserService;

    public FeedLotMediaHandler(
        ILogger<FeedLotMediaHandler> logger,
        ILabelPrintingService labelPrinter,
        IPrinterMediaStateRepository mediaStateRepository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _labelPrinter = labelPrinter;
        _mediaStateRepository = mediaStateRepository;
        _currentUserService = currentUserService;
    }

    public async Task<FeedLotMediaResponse> Handle(
        FeedLotMediaRequest request, CancellationToken cancellationToken)
    {
        var mediaState = await _mediaStateRepository.GetAsync(cancellationToken);
        if (mediaState.RequiresConfirmation(LabelMediaType.LotRound) && !request.MediaChangeConfirmed)
        {
            return new FeedLotMediaResponse { RequiresMediaChangeConfirmation = true };
        }

        var zpl = LotLabelZplBuilder.BuildMediaFeed(request.Dots);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        mediaState.RecordPrint(LabelMediaType.LotRound, _currentUserService.GetCurrentUser().Name ?? "System");
        await _mediaStateRepository.SaveAsync(mediaState, cancellationToken);

        _logger.LogInformation("Fed lot label media forward by {Dots} dots", request.Dots);

        return new FeedLotMediaResponse();
    }
}
