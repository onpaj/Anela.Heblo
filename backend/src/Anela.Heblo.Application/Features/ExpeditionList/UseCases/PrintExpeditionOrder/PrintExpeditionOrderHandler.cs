using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderHandler : IRequestHandler<PrintExpeditionOrderRequest, PrintExpeditionOrderResponse>
{
    // These are already-in-progress / done / cancelled states — printing them would double-print.
    private static readonly IReadOnlyDictionary<int, string> NonPrintableStates = new Dictionary<int, string>
    {
        { -3, "zrušeno/blokováno" },
        { 26, "Balí se" },
        { 52, "Zabaleno" },
        { 70, "Předáno přepravci" },
    };

    private readonly IExpeditionListService _expeditionListService;
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly ILogger<PrintExpeditionOrderHandler> _logger;

    public PrintExpeditionOrderHandler(
        IExpeditionListService expeditionListService,
        IEshopOrderClient eshopOrderClient,
        IOptions<PrintPickingListOptions> options,
        ILogger<PrintExpeditionOrderHandler> logger)
    {
        _expeditionListService = expeditionListService;
        _eshopOrderClient = eshopOrderClient;
        _options = options;
        _logger = logger;
    }

    public async Task<PrintExpeditionOrderResponse> Handle(
        PrintExpeditionOrderRequest request,
        CancellationToken cancellationToken)
    {
        int currentStatusId;
        try
        {
            currentStatusId = await _eshopOrderClient.GetOrderStatusIdAsync(request.OrderCode, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Order {OrderCode} not found in Shoptet", request.OrderCode);
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ShoptetOrderNotFound,
                new Dictionary<string, string> { { "orderCode", request.OrderCode } });
        }

        if (NonPrintableStates.TryGetValue(currentStatusId, out var stateName))
        {
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ExpeditionOrderInvalidState,
                new Dictionary<string, string>
                {
                    { "orderCode", request.OrderCode },
                    { "currentStatusName", stateName },
                });
        }

        var printRequest = new ExpeditionPickingRequest
        {
            OrderCode = request.OrderCode,
            Carriers = new List<Carriers>(),
            DesiredStateId = _options.Value.DesiredStateId,
            ChangeOrderState = true,
            SendToPrinter = true,
        };

        var result = await _expeditionListService.PrintPickingListAsync(
            printRequest, cancellationToken: cancellationToken);

        if (result.TotalCount == 0)
        {
            return new PrintExpeditionOrderResponse(
                ErrorCodes.ExpeditionOrderNotPrinted,
                new Dictionary<string, string> { { "orderCode", request.OrderCode } });
        }

        return new PrintExpeditionOrderResponse();
    }
}
