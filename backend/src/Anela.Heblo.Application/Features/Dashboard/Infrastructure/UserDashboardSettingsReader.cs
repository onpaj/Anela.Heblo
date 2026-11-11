using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

internal class UserDashboardSettingsReader : IUserDashboardSettingsReader
{
    private readonly ITileRegistry _tileRegistry;
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly TimeProvider _timeProvider;

    public UserDashboardSettingsReader(
        ITileRegistry tileRegistry,
        IUserDashboardSettingsRepository repository,
        TimeProvider timeProvider)
    {
        _tileRegistry = tileRegistry;
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<UserDashboardSettingsDto> GetEffectiveSettingsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetByUserIdAsync(userId);
        var now = _timeProvider.GetUtcNow().DateTime;
        var autoShowTiles = UserDashboardSettingsDefaults.AutoShowTiles(_tileRegistry);

        if (settings == null)
        {
            var defaults = UserDashboardSettingsDefaults.BuildDefault(userId, autoShowTiles, now);
            return new UserDashboardSettingsDto
            {
                Tiles = defaults.Tiles.Select(t => new UserDashboardTileDto
                {
                    TileId = t.TileId,
                    IsVisible = t.IsVisible,
                    DisplayOrder = t.DisplayOrder
                }).ToArray(),
                LastModified = defaults.LastModified
            };
        }

        return new UserDashboardSettingsDto
        {
            Tiles = UserDashboardSettingsDefaults.ProjectMissing(settings, autoShowTiles, now).ToArray(),
            LastModified = settings.LastModified
        };
    }
}
