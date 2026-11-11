using Anela.Heblo.Domain.Features.Dashboard;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

internal interface IUserDashboardSettingsProvisioner
{
    /// <summary>
    /// Ensures persisted dashboard settings exist for the user: creates defaults for a new user and
    /// back-fills missing AutoShow tiles for an existing one (writing only when something changed).
    /// Acquires <see cref="IUserDashboardSettingsLock"/> internally and releases it before returning.
    /// WARNING: the lock is non-reentrant - this must be called outside any held
    /// <see cref="IUserDashboardSettingsLock"/> for the same user, or it will deadlock.
    /// </summary>
    Task<UserDashboardSettings> EnsureProvisionedAsync(string userId, CancellationToken cancellationToken = default);
}
