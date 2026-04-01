using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderProcessingResponse : BaseResponse
{
    public BlockOrderProcessingResponse()
    {
    }

    public BlockOrderProcessingResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params)
    {
    }
}
