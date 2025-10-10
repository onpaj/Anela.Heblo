using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

public class SaveUserSettingsHandler : IRequestHandler<SaveUserSettingsRequest, SaveUserSettingsResponse>
{
    private readonly IDashboardService _dashboardService;
    private readonly TimeProvider _timeProvider;

    public SaveUserSettingsHandler(
        IDashboardService dashboardService,
        TimeProvider timeProvider
        )
    {
        _dashboardService = dashboardService;
        _timeProvider = timeProvider;
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
                existingTile.LastModified = _timeProvider.GetUtcNow().DateTime;
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
                    LastModified = _timeProvider.GetUtcNow().DateTime,
                    DashboardSettings = settings
                });
            }
        }
        }
        
        settings.LastModified = _timeProvider.GetUtcNow().DateTime;
        await _dashboardService.SaveUserSettingsAsync(userId, settings);
        
        return new SaveUserSettingsResponse();
    }
}