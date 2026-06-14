using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

internal sealed class PlaudTokenStore : IPlaudTokenStore
{
    private readonly SecretClient _secretClient;
    private readonly IOptions<PlaudCredentialsOptions> _options;
    private readonly ILogger<PlaudTokenStore> _logger;
    private readonly string _homeDir;

    public PlaudTokenStore(
        SecretClient secretClient,
        IOptions<PlaudCredentialsOptions> options,
        ILogger<PlaudTokenStore> logger,
        string? homeDirOverride = null)
    {
        _secretClient = secretClient;
        _options = options;
        _logger = logger;
        _homeDir = homeDirOverride
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private string TokensPath => Path.Combine(_homeDir, ".plaud", "tokens.json");

    public async Task<PlaudTokens> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(TokensPath))
            throw new InvalidOperationException(
                $"Plaud tokens.json not found at {TokensPath}. PlaudTokenBootstrapper must run first.");

        var json = await File.ReadAllTextAsync(TokensPath, ct);
        return JsonSerializer.Deserialize<PlaudTokens>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Plaud tokens from disk.");
    }

    public async Task<PlaudTokenSaveResult> SaveAsync(PlaudTokens tokens, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(tokens);

        var tokensPath = TokensPath;
        var dir = Path.GetDirectoryName(tokensPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(tokensPath, json, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tokensPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        try
        {
            await _secretClient.SetSecretAsync(_options.Value.TokensJsonSecretName, json, ct);
            return new PlaudTokenSaveResult(KeyVaultWriteFailed: false, KeyVaultError: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plaud token KV write failed (disk update succeeded)");
            return new PlaudTokenSaveResult(KeyVaultWriteFailed: true, KeyVaultError: ex);
        }
    }
}
