using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;

public class FeedLotMediaRequest : IRequest<FeedLotMediaResponse>
{
    public int Dots { get; set; }

    /// <summary>Set by the client to confirm the operator has changed and calibrated the media when switching type.</summary>
    public bool MediaChangeConfirmed { get; set; }
}
