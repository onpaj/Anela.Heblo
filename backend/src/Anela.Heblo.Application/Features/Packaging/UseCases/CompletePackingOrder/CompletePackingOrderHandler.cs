using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;

public class CompletePackingOrderHandler
    : IRequestHandler<CompletePackingOrderRequest, CompletePackingOrderResponse>
{
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly ILogger<CompletePackingOrderHandler> _logger;

    public CompletePackingOrderHandler(
        IEshopOrderClient eshopOrderClient,
        ILogger<CompletePackingOrderHandler> logger)
    {
        _eshopOrderClient = eshopOrderClient;
        _logger = logger;
    }

    public async Task<CompletePackingOrderResponse> Handle(
        CompletePackingOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _eshopOrderClient.MarkAsPackedAsync(request.OrderCode, cancellationToken);
            return new CompletePackingOrderResponse(completed: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to mark order {OrderCode} as packed during packing completion",
                request.OrderCode);
            return new CompletePackingOrderResponse(ErrorCodes.PackingCompletionFailed);
        }
    }
}
