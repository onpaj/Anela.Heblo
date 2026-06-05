using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;

public class GetPackingOrderHandler : IRequestHandler<GetPackingOrderRequest, GetPackingOrderResponse>
{
    private readonly IPackingOrderClient _client;
    private readonly IOptions<ShoptetOrdersSettings> _settings;
    private readonly ILogger<GetPackingOrderHandler> _logger;

    public GetPackingOrderHandler(
        IPackingOrderClient client,
        IOptions<ShoptetOrdersSettings> settings,
        ILogger<GetPackingOrderHandler> logger)
    {
        _client = client;
        _settings = settings;
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

            var isEligible = order.StatusId == _settings.Value.PackingStateId;

            return new GetPackingOrderResponse
            {
                Code = order.Code,
                CustomerName = order.CustomerName,
                ShippingMethodName = order.ShippingMethodName,
                Cooling = order.Cooling,
                IsCooled = order.IsCooled,
                Eligibility = new PackingEligibility
                {
                    IsEligible = isEligible,
                    WarningTitle = isEligible ? null : "Objednávka není ve stavu „Balí se“",
                    WarningBody = isEligible ? null : "Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.",
                },
                CustomerNote = order.CustomerNote,
                EshopNote = order.EshopNote,
                Items = order.Items
                    .Select(i => new PackingOrderItemDto
                    {
                        Name = i.Name,
                        Quantity = i.Quantity,
                        ImageUrl = i.ImageUrl,
                        SetName = i.SetName,
                    })
                    .ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packing order {OrderCode}", request.Code);
            return new GetPackingOrderResponse(ErrorCodes.InternalServerError);
        }
    }
}
