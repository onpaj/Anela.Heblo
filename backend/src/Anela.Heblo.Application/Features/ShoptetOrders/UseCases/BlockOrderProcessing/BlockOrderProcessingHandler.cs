using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderProcessingHandler : IRequestHandler<BlockOrderProcessingRequest, BlockOrderProcessingResponse>
{
    private readonly IEshopOrderClient _eshopOrderClient;
    private readonly IOptions<ShoptetOrdersSettings> _settings;
    private readonly ILogger<BlockOrderProcessingHandler> _logger;

    public BlockOrderProcessingHandler(
        IEshopOrderClient eshopOrderClient,
        IOptions<ShoptetOrdersSettings> settings,
        ILogger<BlockOrderProcessingHandler> logger)
    {
        _eshopOrderClient = eshopOrderClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<BlockOrderProcessingResponse> Handle(
        BlockOrderProcessingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentStatusId = await _eshopOrderClient.GetOrderStatusIdAsync(
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

            await _eshopOrderClient.UpdateStatusAsync(
                request.OrderCode, _settings.Value.BlockedStatusId, cancellationToken);

            await _eshopOrderClient.SetInternalNoteAsync(
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
