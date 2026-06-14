namespace Anela.Heblo.Adapters.Plaud;

public interface IPlaudTokenManager
{
    /// <summary>
    /// No-op on the happy path. If the cached token is inside ExpiryBuffer of expiry,
    /// triggers a single-flight refresh, persists via IPlaudTokenStore, and updates the cache.
    /// </summary>
    Task EnsureFreshAsync(CancellationToken ct);

    /// <summary>
    /// Forces a refresh now (called after the CLI returns AUTH_FAILED).
    /// Returns true on success (caller may retry the CLI). Returns false when the refresh itself
    /// fails — caller surfaces PlaudAuthExpiredException so the runbook fires.
    /// Single-flight: concurrent callers await the same refresh task.
    /// </summary>
    Task<bool> ForceRefreshAsync(CancellationToken ct);
}
