using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

internal sealed class UserDashboardSettingsMutator : IUserDashboardSettingsMutator
{
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly IMediator _mediator;

    public UserDashboardSettingsMutator(
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider,
        IMediator mediator)
    {
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
        _mediator = mediator;
    }

    public async Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = string.IsNullOrEmpty(userId) ? "anonymous" : userId;

        // Trigger provisioning outside the write lock (lock is non-reentrant).
        await _mediator.Send(new GetUserSettingsRequest { UserId = resolvedUserId }, cancellationToken);

        await using var lockHandle = await _lock.AcquireAsync(resolvedUserId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(resolvedUserId);
        if (settings == null)
        {
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: false,
                TileFound: false,
                TileAppended: false);
        }

        var now = _timeProvider.GetUtcNow().DateTime;
        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileId);

        if (existingTile != null)
        {
            onTileFound(settings, existingTile);
            existingTile.LastModified = now;
            settings.LastModified = now;
            await _repository.UpdateAsync(settings);
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: true,
                TileFound: true,
                TileAppended: false);
        }

        if (onTileMissing == null)
        {
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: true,
                TileFound: false,
                TileAppended: false);
        }

        var newTile = onTileMissing(settings, resolvedUserId);
        if (newTile == null)
        {
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: true,
                TileFound: false,
                TileAppended: false);
        }

        newTile.LastModified = now;
        settings.Tiles.Add(newTile);
        settings.LastModified = now;
        await _repository.UpdateAsync(settings);
        return new UserDashboardSettingsMutationResult(
            SettingsLoaded: true,
            TileFound: false,
            TileAppended: true);
    }
}
