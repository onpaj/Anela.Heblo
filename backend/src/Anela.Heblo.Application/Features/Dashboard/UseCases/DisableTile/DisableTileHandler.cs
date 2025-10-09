using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

public class DisableTileHandler : IRequestHandler<DisableTileRequest, DisableTileResponse>
{
    private readonly IDashboardService _dashboardService;

    public DisableTileHandler(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public async Task<DisableTileResponse> Handle(DisableTileRequest request, CancellationToken cancellationToken)
    {
        var settings = await _dashboardService.GetUserSettingsAsync(request.UserId);
        
        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == request.TileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = false;
            existingTile.LastModified = DateTime.UtcNow;
            settings.LastModified = DateTime.UtcNow;
            
            await _dashboardService.SaveUserSettingsAsync(request.UserId, settings);
        }

        return new DisableTileResponse();
    }
}