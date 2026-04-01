using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.Logistics.Picking;
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
        var printRequest = new PrintPickingListRequest
        {
            Carriers = PrintPickingListRequest.DefaultCarriers,
            SourceStateId = _options.Value.FixSourceStateId,
            DesiredStateId = _options.Value.DesiredStateId,
            ChangeOrderState = _options.Value.ChangeOrderStateByDefault,
            SendToPrinter = _options.Value.SendToPrinterByDefault,
        };

        var result = await _expeditionListService.PrintPickingListAsync(
            printRequest,
            cancellationToken: cancellationToken);

        return new RunExpeditionListPrintFixResponse
        {
            Success = true,
            TotalCount = result.TotalCount,
        };
    }
}
