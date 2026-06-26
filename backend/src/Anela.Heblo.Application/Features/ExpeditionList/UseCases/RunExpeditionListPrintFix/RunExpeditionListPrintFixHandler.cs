using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;

public class RunExpeditionListPrintFixHandler : IRequestHandler<RunExpeditionListPrintFixRequest, RunExpeditionListPrintFixResponse>
{
    private readonly IExpeditionListService _expeditionListService;
    private readonly IOptions<PrintPickingListOptions> _options;

    public RunExpeditionListPrintFixHandler(
        IExpeditionListService expeditionListService,
        IOptions<PrintPickingListOptions> options)
    {
        _expeditionListService = expeditionListService;
        _options = options;
    }

    public async Task<RunExpeditionListPrintFixResponse> Handle(
        RunExpeditionListPrintFixRequest request,
        CancellationToken cancellationToken)
    {
        var printRequest = new ExpeditionPickingRequest
        {
            Carriers = ExpeditionPickingRequest.DefaultCarriers,
            SourceStateId = _options.Value.FixSourceStateId,
            DesiredStateId = _options.Value.DesiredStateId,
            NoteStateId = _options.Value.NoteStateId,
            ChangeOrderState = _options.Value.ChangeOrderStateByDefault,
            SendToPrinter = _options.Value.SendToPrinterByDefault,
        };

        var result = await _expeditionListService.PrintPickingListAsync(
            printRequest,
            cancellationToken: cancellationToken);

        return new RunExpeditionListPrintFixResponse
        {
            TotalCount = result.TotalCount,
            SkippedCount = result.SkippedCount,
        };
    }
}
