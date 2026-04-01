using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ShoptetOrders;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderProcessingHandler : IRequestHandler<BlockOrderProcessingRequest, BlockOrderProcessingResponse>
{
    private readonly IShoptetOrderClient _shoptetOrderClient;
    private readonly IOptions<ShoptetOrdersSettings> _settings;
    private readonly ILogger<BlockOrderProcessingHandler> _logger;

    public BlockOrderProcessingHandler(
        IShoptetOrderClient shoptetOrderClient,
        IOptions<ShoptetOrdersSettings> settings,
        ILogger<BlockOrderProcessingHandler> logger)
    {
        _shoptetOrderClient = shoptetOrderClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<BlockOrderProcessingResponse> Handle(
        BlockOrderProcessingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentStatusId = await _shoptetOrderClient.GetOrderStatusIdAsync(
                request.OrderCode, cancellationToken);

            if (!_settings.Value.AllowedBlockSourceStateIds.Contains(currentStatusId))
            {
                return new BlockOrderProcessingResponse(
                    ErrorCodes.ShoptetOrderInvalidSourceState,
                    new Dictionary<string, string>
                    {
                        { "orderCode", request.OrderCode },
                        { "currentStatusId", currentStatusId.ToString() },
                    });
            }

            await _shoptetOrderClient.UpdateStatusAsync(
                request.OrderCode, _settings.Value.BlockedStatusId, cancellationToken);

            await _shoptetOrderClient.SetInternalNoteAsync(
                request.OrderCode, request.Note, cancellationToken);

            return new BlockOrderProcessingResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to block order {OrderCode}", request.OrderCode);
            return new BlockOrderProcessingResponse(ErrorCodes.InternalServerError);
        }
    }
}
