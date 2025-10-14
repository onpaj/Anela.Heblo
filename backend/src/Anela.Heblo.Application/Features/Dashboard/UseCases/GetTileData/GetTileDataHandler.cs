using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataHandler : IRequestHandler<GetTileDataRequest, GetTileDataResponse>
{
    private readonly IDashboardService _dashboardService;

    public GetTileDataHandler(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public async Task<GetTileDataResponse> Handle(GetTileDataRequest request, CancellationToken cancellationToken)
    {
        var userId = string.IsNullOrEmpty(request.UserId) ? "anonymous" : request.UserId;
        var tileData = await _dashboardService.GetTileDataAsync(userId, request.TileParameters);

        var result = tileData.Select(td => new DashboardTileDto
        {
            TileId = td.TileId,
            Title = td.Title,
            Description = td.Description,
            Size = td.Size.ToString(),
            Category = td.Category.ToString(),
            DefaultEnabled = td.DefaultEnabled,
            AutoShow = td.AutoShow,
            RequiredPermissions = td.RequiredPermissions,
            Data = td.Data
        });

        return new GetTileDataResponse
        {
            Tiles = result
        };
    }
}