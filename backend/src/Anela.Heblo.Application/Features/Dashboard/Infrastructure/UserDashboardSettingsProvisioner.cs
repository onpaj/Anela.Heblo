using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

internal class UserDashboardSettingsProvisioner : IUserDashboardSettingsProvisioner
{
    private readonly ITileRegistry _tileRegistry;
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;

    public UserDashboardSettingsProvisioner(
        ITileRegistry tileRegistry,
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider)
    {
        _tileRegistry = tileRegistry;
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
    }

    public async Task<UserDashboardSettings> EnsureProvisionedAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var _ = await _lock.AcquireAsync(userId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(userId);
        var now = _timeProvider.GetUtcNow().DateTime;
        var autoShowTiles = UserDashboardSettingsDefaults.AutoShowTiles(_tileRegistry);

        if (settings == null)
        {
            settings = UserDashboardSettingsDefaults.BuildDefault(userId, autoShowTiles, now);
            await _repository.AddAsync(settings);
        }
        else if (UserDashboardSettingsDefaults.BackfillMissing(settings, autoShowTiles, now))
        {
            await _repository.UpdateAsync(settings);
        }

        return settings;
    }
}
