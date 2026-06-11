using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

public class EnableTileRequest : IRequest<EnableTileResponse>
{
    public string TileId { get; set; } = string.Empty;
}