namespace Anela.Heblo.Adapters.Plaud;

/// <summary>
/// Refreshes the Plaud auth token: reads the current token from disk, calls the Plaud OAuth
/// refresh endpoint, validates the response, and persists the rotated token back to disk and
/// (when Key Vault is configured) to Key Vault so container restarts pick up the fresh value.
/// </summary>
public interface IPlaudTokenRefresher
{
    Task RefreshAsync(CancellationToken ct = default);
}
