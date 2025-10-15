using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

public class EnableTileRequest : IRequest<EnableTileResponse>
{
    public string? UserId { get; set; }
    public string TileId { get; set; } = string.Empty;
}