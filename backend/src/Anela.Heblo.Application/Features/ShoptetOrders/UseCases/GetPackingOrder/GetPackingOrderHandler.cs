using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class GetPackingOrderHandler : IRequestHandler<GetPackingOrderRequest, GetPackingOrderResponse>
{
    private readonly IPackingOrderClient _client;
    private readonly ILogger<GetPackingOrderHandler> _logger;

    public GetPackingOrderHandler(
        IPackingOrderClient client,
        ILogger<GetPackingOrderHandler> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<GetPackingOrderResponse> Handle(
        GetPackingOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await _client.GetPackingOrderAsync(request.Code, cancellationToken);

            if (order == null)
            {
                return new GetPackingOrderResponse(
                    ErrorCodes.ShoptetOrderNotFound,
                    new Dictionary<string, string> { { "orderCode", request.Code } });
            }

            return new GetPackingOrderResponse
            {
                Code = order.Code,
                CustomerName = order.CustomerName,
                ShippingMethodName = order.ShippingMethodName,
                Cooling = order.Cooling,
                IsCooled = order.IsCooled,
                Items = order.Items,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packing order {OrderCode}", request.Code);
            return new GetPackingOrderResponse(ErrorCodes.InternalServerError);
        }
    }
}
