using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

public class DisableTileHandler : IRequestHandler<DisableTileRequest, DisableTileResponse>
{
    private readonly IDashboardService _dashboardService;
    private readonly TimeProvider _timeProvider;

    public DisableTileHandler(
        IDashboardService dashboardService,
        TimeProvider timeProvider)
    {
        _dashboardService = dashboardService;
        _timeProvider = timeProvider;
    }

    public async Task<DisableTileResponse> Handle(DisableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new DisableTileResponse(Anela.Heblo.Application.Shared.ErrorCodes.RequiredFieldMissing);
        }

        var userId = string.IsNullOrEmpty(request.UserId) ? "anonymous" : request.UserId;
        var settings = await _dashboardService.GetUserSettingsAsync(userId);

        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == request.TileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = false;
            existingTile.LastModified = _timeProvider.GetUtcNow().DateTime;
            settings.LastModified = _timeProvider.GetUtcNow().DateTime;

            await _dashboardService.SaveUserSettingsAsync(userId, settings);
        }

        return new DisableTileResponse();
    }
}