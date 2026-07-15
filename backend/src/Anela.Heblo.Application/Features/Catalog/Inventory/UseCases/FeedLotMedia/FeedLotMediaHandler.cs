using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;

public class FeedLotMediaHandler
    : IRequestHandler<FeedLotMediaRequest, FeedLotMediaResponse>
{
    private readonly ILogger<FeedLotMediaHandler> _logger;
    private readonly ILabelPrintingService _labelPrinter;

    public FeedLotMediaHandler(
        ILogger<FeedLotMediaHandler> logger,
        ILabelPrintingService labelPrinter)
    {
        _logger = logger;
        _labelPrinter = labelPrinter;
    }

    public async Task<FeedLotMediaResponse> Handle(
        FeedLotMediaRequest request, CancellationToken cancellationToken)
    {
        var zpl = LotLabelZplBuilder.BuildMediaFeed(request.Dots);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        _logger.LogInformation("Fed lot label media forward by {Dots} dots", request.Dots);

        return new FeedLotMediaResponse();
    }
}
