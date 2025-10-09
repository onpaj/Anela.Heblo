using Anela.Heblo.Application.Features.Dashboard.Contracts;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;

public class GetAvailableTilesResponse
{
    public IEnumerable<DashboardTileDto> Tiles { get; set; } = Array.Empty<DashboardTileDto>();
}