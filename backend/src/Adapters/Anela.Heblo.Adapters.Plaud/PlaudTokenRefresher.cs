using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Plaud;

/// <inheritdoc />
public sealed class PlaudTokenRefresher : IPlaudTokenRefresher
{
    // The Key Vault secret mirrors the on-disk ~/.plaud/tokens.json blob. PlaudTokenBootstrapper
    // re-seeds disk from this value on container start, so keeping it fresh prevents a restart from
    // reintroducing an expired token.
    private const string KeyVaultSecretName = "Plaud--TokensJson";

    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly SecretClient? _secretClient;
    private readonly ILogger<PlaudTokenRefresher> _logger;
    private readonly string _tokensFilePath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public PlaudTokenRefresher(
        IPlaudTokenRefreshClient refreshClient,
        ILogger<PlaudTokenRefresher> logger,
        SecretClient? secretClient = null)
        : this(refreshClient, logger, secretClient, DefaultTokensPath())
    { }

    internal PlaudTokenRefresher(
        IPlaudTokenRefreshClient refreshClient,
        ILogger<PlaudTokenRefresher> logger,
        SecretClient? secretClient,
        string tokensFilePath)
    {
        _refreshClient = refreshClient;
        _logger = logger;
        _secretClient = secretClient;
        _tokensFilePath = tokensFilePath;
    }

    private static string DefaultTokensPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".plaud",
            "tokens.json");

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Serialize concurrent refreshes so multiple in-flight CLI calls don't double-refresh and
        // race each other writing the tokens file.
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_tokensFilePath))
                throw new InvalidOperationException(
                    $"Plaud tokens file not found at {_tokensFilePath}. Cannot refresh.");

            var diskJson = await File.ReadAllTextAsync(_tokensFilePath, ct);
            var diskTokens = JsonSerializer.Deserialize<PlaudTokens>(diskJson)
                ?? throw new InvalidOperationException("Failed to deserialize Plaud tokens from disk.");

            var newTokens = await _refreshClient.RefreshAsync(diskTokens.RefreshToken, ct);

            // Validate before persisting — never overwrite a good token with garbage.
            if (string.IsNullOrEmpty(newTokens.AccessToken) || string.IsNullOrEmpty(newTokens.RefreshToken))
                throw new InvalidOperationException(
                    "Plaud refresh response has empty tokens. Refusing to persist.");

            if (newTokens.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                throw new InvalidOperationException(
                    $"Plaud refresh response has expires_at={newTokens.ExpiresAt} in the past. Refusing to persist.");

            var newJson = JsonSerializer.Serialize(newTokens);

            // Write disk first — the running process (and an immediate CLI retry) needs the fresh
            // token even if Key Vault persistence later fails.
            await File.WriteAllTextAsync(_tokensFilePath, newJson, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_tokensFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            // Key Vault persistence is best-effort: disk already healed the running process, so a KV
            // failure must not fail the refresh. It only means a future restart could re-seed a
            // stale token — worth an error log, not an exception.
            if (_secretClient is not null)
            {
                try
                {
                    await _secretClient.SetSecretAsync(KeyVaultSecretName, newJson, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Plaud token refreshed on disk but Key Vault persistence failed. A container restart may re-seed a stale token.");
                }
            }

            _logger.LogInformation("Plaud token refreshed. expires_at={ExpiresAt}", newTokens.ExpiresAt);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
