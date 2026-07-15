using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;

public class FeedLotMediaResponse : BaseResponse
{
    public FeedLotMediaResponse() : base() { }

    public FeedLotMediaResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
