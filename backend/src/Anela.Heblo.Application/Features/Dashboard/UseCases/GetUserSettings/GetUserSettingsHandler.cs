using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsHandler : IRequestHandler<GetUserSettingsRequest, GetUserSettingsResponse>
{
    private readonly ITileRegistry _tileRegistry;
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;

    public GetUserSettingsHandler(
        ITileRegistry tileRegistry,
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService)
    {
        _tileRegistry = tileRegistry;
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
    }

    public async Task<GetUserSettingsResponse> Handle(GetUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetCurrentUser().Id;
        var userId = string.IsNullOrEmpty(currentUserId) ? "anonymous" : currentUserId;

        await using var _ = await _lock.AcquireAsync(userId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(userId);
        var now = _timeProvider.GetUtcNow().DateTime;

        var autoShowTiles = _tileRegistry.GetAvailableTiles()
            .Where(t => t.DefaultEnabled && t.AutoShow)
            .ToList();

        if (settings == null)
        {
            // FR-1: Create default settings for new user
            settings = new UserDashboardSettings
            {
                UserId = userId,
                LastModified = now,
                Tiles = autoShowTiles.Select((tile, index) => new UserDashboardTile
                {
                    UserId = userId,
                    TileId = tile.TileId,
                    IsVisible = true,
                    DisplayOrder = index,
                    LastModified = now,
                    DashboardSettings = null!
                }).ToList()
            };

            // Fix the DashboardSettings back-reference after settings is assigned
            foreach (var tile in settings.Tiles)
                tile.DashboardSettings = settings;

            await _repository.AddAsync(settings);
        }
        else
        {
            // FR-2: Back-fill new AutoShow tiles for existing users
            var existingTileIds = settings.Tiles.Select(t => t.TileId).ToHashSet();
            var newAutoShowTiles = autoShowTiles
                .Where(t => !existingTileIds.Contains(t.TileId))
                .ToList();

            if (newAutoShowTiles.Count > 0)
            {
                var maxOrder = settings.Tiles.Count > 0 ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
                for (var i = 0; i < newAutoShowTiles.Count; i++)
                {
                    settings.Tiles.Add(new UserDashboardTile
                    {
                        UserId = userId,
                        TileId = newAutoShowTiles[i].TileId,
                        IsVisible = true,
                        DisplayOrder = maxOrder + i + 1,
                        LastModified = now,
                        DashboardSettings = settings
                    });
                }

                settings.LastModified = now;
                await _repository.UpdateAsync(settings);
            }
        }

        return new GetUserSettingsResponse
        {
            Settings = new UserDashboardSettingsDto
            {
                Tiles = settings.Tiles.Select(t => new UserDashboardTileDto
                {
                    TileId = t.TileId,
                    IsVisible = t.IsVisible,
                    DisplayOrder = t.DisplayOrder
                }).ToArray(),
                LastModified = settings.LastModified
            }
        };
    }
}
