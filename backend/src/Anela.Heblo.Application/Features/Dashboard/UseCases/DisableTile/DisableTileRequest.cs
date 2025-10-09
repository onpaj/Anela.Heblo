using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

public class DisableTileRequest : IRequest<DisableTileResponse>
{
    public string UserId { get; set; } = string.Empty;
    public string TileId { get; set; } = string.Empty;
}