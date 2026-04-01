using MediatR;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderProcessingRequest : IRequest<BlockOrderProcessingResponse>
{
    public string OrderCode { get; set; } = null!;
    public string Note { get; set; } = null!;
}
