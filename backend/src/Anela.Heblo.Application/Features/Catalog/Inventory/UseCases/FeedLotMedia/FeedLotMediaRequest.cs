using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;

public class FeedLotMediaRequest : IRequest<FeedLotMediaResponse>
{
    public int Dots { get; set; }
}
