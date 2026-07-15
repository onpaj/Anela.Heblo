using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;

public class FeedLotMediaResponse : BaseResponse
{
    /// <summary>True when the feed was blocked because the media type changed; the client must confirm and resend.</summary>
    public bool RequiresMediaChangeConfirmation { get; set; }

    public FeedLotMediaResponse() : base() { }

    public FeedLotMediaResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
