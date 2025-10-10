using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataResponse : BaseResponse
{
    public IEnumerable<DashboardTileDto> Tiles { get; set; } = Array.Empty<DashboardTileDto>();
}