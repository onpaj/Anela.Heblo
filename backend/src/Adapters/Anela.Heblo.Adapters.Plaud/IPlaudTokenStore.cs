namespace Anela.Heblo.Adapters.Plaud;

public interface IPlaudTokenStore
{
    /// <summary>
    /// Loads the current tokens from disk (~/.plaud/tokens.json).
    /// Throws if the disk file is missing or unreadable — PlaudTokenBootstrapper must run first.
    /// </summary>
    Task<PlaudTokens> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Persists the tokens disk-first, then to Key Vault.
    /// Throws if the disk write fails (fatal — CLI would still see the old token).
    /// If the KV write fails, returns KeyVaultWriteFailed=true with the captured exception.
    /// </summary>
    Task<PlaudTokenSaveResult> SaveAsync(PlaudTokens tokens, CancellationToken ct);
}
