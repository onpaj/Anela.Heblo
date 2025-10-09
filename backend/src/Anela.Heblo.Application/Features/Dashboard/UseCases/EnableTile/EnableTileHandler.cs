using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

public class EnableTileHandler : IRequestHandler<EnableTileRequest, EnableTileResponse>
{
    private readonly IDashboardService _dashboardService;

    public EnableTileHandler(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public async Task<EnableTileResponse> Handle(EnableTileRequest request, CancellationToken cancellationToken)
    {
        var settings = await _dashboardService.GetUserSettingsAsync(request.UserId);
        
        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == request.TileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = true;
            existingTile.LastModified = DateTime.UtcNow;
        }
        else
        {
            // Add new tile with next display order
            var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
            settings.Tiles.Add(new UserDashboardTile
            {
                UserId = request.UserId,
                TileId = request.TileId,
                IsVisible = true,
                DisplayOrder = maxOrder + 1,
                LastModified = DateTime.UtcNow,
                DashboardSettings = settings
            });
        }
        
        settings.LastModified = DateTime.UtcNow;
        await _dashboardService.SaveUserSettingsAsync(request.UserId, settings);

        return new EnableTileResponse();
    }
}