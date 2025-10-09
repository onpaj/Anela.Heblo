using Anela.Heblo.Application.Features.Dashboard.Contracts;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataResponse
{
    public IEnumerable<DashboardTileDto> Tiles { get; set; } = Array.Empty<DashboardTileDto>();
}