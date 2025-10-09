using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

public class SaveUserSettingsHandler : IRequestHandler<SaveUserSettingsRequest, SaveUserSettingsResponse>
{
    private readonly IDashboardService _dashboardService;

    public SaveUserSettingsHandler(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public async Task<SaveUserSettingsResponse> Handle(SaveUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await _dashboardService.GetUserSettingsAsync(request.UserId);
        
        // Update tile settings
        foreach (var tileDto in request.Tiles)
        {
            var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileDto.TileId);
            if (existingTile != null)
            {
                existingTile.IsVisible = tileDto.IsVisible;
                existingTile.DisplayOrder = tileDto.DisplayOrder;
                existingTile.LastModified = DateTime.UtcNow;
            }
            else
            {
                // Add new tile
                settings.Tiles.Add(new UserDashboardTile
                {
                    UserId = request.UserId,
                    TileId = tileDto.TileId,
                    IsVisible = tileDto.IsVisible,
                    DisplayOrder = tileDto.DisplayOrder,
                    LastModified = DateTime.UtcNow,
                    DashboardSettings = settings
                });
            }
        }
        
        settings.LastModified = DateTime.UtcNow;
        await _dashboardService.SaveUserSettingsAsync(request.UserId, settings);
        
        return new SaveUserSettingsResponse();
    }
}