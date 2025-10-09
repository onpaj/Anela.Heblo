using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsHandler : IRequestHandler<GetUserSettingsRequest, GetUserSettingsResponse>
{
    private readonly IDashboardService _dashboardService;

    public GetUserSettingsHandler(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public async Task<GetUserSettingsResponse> Handle(GetUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await _dashboardService.GetUserSettingsAsync(request.UserId);
        
        var result = new UserDashboardSettingsDto
        {
            Tiles = settings.Tiles.Select(t => new UserDashboardTileDto
            {
                TileId = t.TileId,
                IsVisible = t.IsVisible,
                DisplayOrder = t.DisplayOrder
            }).ToArray(),
            LastModified = settings.LastModified
        };

        return new GetUserSettingsResponse
        {
            Settings = result
        };
    }
}