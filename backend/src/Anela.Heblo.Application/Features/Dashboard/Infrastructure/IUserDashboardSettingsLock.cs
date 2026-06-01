namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

public interface IUserDashboardSettingsLock
{
    /// <summary>
    /// Acquires the per-user write lock.
    /// WARNING: This lock is non-reentrant. Never call AcquireAsync while already holding
    /// the lock for the same userId — it will deadlock.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(string userId, CancellationToken cancellationToken = default);
}
