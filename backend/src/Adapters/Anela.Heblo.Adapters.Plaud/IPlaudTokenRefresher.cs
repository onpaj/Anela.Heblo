namespace Anela.Heblo.Adapters.Plaud;

/// <summary>
/// Refreshes the Plaud auth token: reads the current token from disk, calls the Plaud OAuth
/// refresh endpoint, validates the response, and persists the rotated token back to disk and
/// (when Key Vault is configured) to Key Vault so container restarts pick up the fresh value.
/// </summary>
public interface IPlaudTokenRefresher
{
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Mirrors the current on-disk <c>~/.plaud/tokens.json</c> to Key Vault when it has changed.
    /// The Plaud CLI rotates the refresh token on disk during normal use without going through
    /// <see cref="RefreshAsync"/>; without this sync those rotations never reach Key Vault, so a
    /// container restart re-seeds a stale token. Best-effort — never throws.
    /// </summary>
    Task SyncToKeyVaultAsync(CancellationToken ct = default);
}
