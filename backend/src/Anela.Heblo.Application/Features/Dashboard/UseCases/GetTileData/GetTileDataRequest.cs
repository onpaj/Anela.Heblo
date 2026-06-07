using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataRequest : IRequest<GetTileDataResponse>
{
    public Dictionary<string, string>? TileParameters { get; set; }
}