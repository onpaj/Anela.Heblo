using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;

public class GetAvailableTilesResponse : BaseResponse
{
    public IEnumerable<DashboardTileDto> Tiles { get; set; } = Array.Empty<DashboardTileDto>();
}