using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

public class EnableTileHandler : IRequestHandler<EnableTileRequest, EnableTileResponse>
{
    private readonly IDashboardService _dashboardService;
    private readonly TimeProvider _timeProvider;

    public EnableTileHandler(
        IDashboardService dashboardService,
        TimeProvider timeProvider)
    {
        _dashboardService = dashboardService;
        _timeProvider = timeProvider;
    }

    public async Task<EnableTileResponse> Handle(EnableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new EnableTileResponse(Anela.Heblo.Application.Shared.ErrorCodes.RequiredFieldMissing);
        }
        
        var userId = string.IsNullOrEmpty(request.UserId) ? "anonymous" : request.UserId;
        var settings = await _dashboardService.GetUserSettingsAsync(userId);
        
        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == request.TileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = true;
            existingTile.LastModified = _timeProvider.GetUtcNow().DateTime;
        }
        else
        {
            // Add new tile with next display order
            var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
            settings.Tiles.Add(new UserDashboardTile
            {
                UserId = userId,
                TileId = request.TileId,
                IsVisible = true,
                DisplayOrder = maxOrder + 1,
                LastModified = _timeProvider.GetUtcNow().DateTime,
                DashboardSettings = settings
            });
        }
        
        settings.LastModified = _timeProvider.GetUtcNow().DateTime;
        await _dashboardService.SaveUserSettingsAsync(userId, settings);

        return new EnableTileResponse();
    }
}