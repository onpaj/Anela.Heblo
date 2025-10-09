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
        var userId = string.IsNullOrEmpty(request.UserId) ? "anonymous" : request.UserId;
        var settings = await _dashboardService.GetUserSettingsAsync(userId);
        
        // Update tile settings
        if (request.Tiles != null)
        {
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
                    UserId = userId,
                    TileId = tileDto.TileId,
                    IsVisible = tileDto.IsVisible,
                    DisplayOrder = tileDto.DisplayOrder,
                    LastModified = DateTime.UtcNow,
                    DashboardSettings = settings
                });
            }
        }
        }
        
        settings.LastModified = DateTime.UtcNow;
        await _dashboardService.SaveUserSettingsAsync(userId, settings);
        
        return new SaveUserSettingsResponse();
    }
}