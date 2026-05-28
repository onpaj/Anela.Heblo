using System.Text.Json;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Azure.Security.KeyVault.Secrets;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Plaud;

public class PlaudTokenRefreshJob : IRecurringJob
{
    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly SecretClient _secretClient;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<PlaudTokenRefreshJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "plaud-token-refresh",
        DisplayName = "Plaud — refresh auth token",
        Description = "Calls Plaud OAuth refresh endpoint weekly and persists the rotated token back to Key Vault so container restarts pick up the fresh value.",
        CronExpression = "0 4 * * 0",
        DefaultIsEnabled = false
    };

    public PlaudTokenRefreshJob(
        IPlaudTokenRefreshClient refreshClient,
        SecretClient secretClient,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PlaudTokenRefreshJob> logger)
    {
        _refreshClient = refreshClient;
        _secretClient = secretClient;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tokensPath = Path.Combine(homeDir, ".plaud", "tokens.json");

        if (!File.Exists(tokensPath))
            throw new InvalidOperationException(
                $"Plaud tokens file not found at {tokensPath}. Run PlaudTokenBootstrapper first.");

        var diskJson = await File.ReadAllTextAsync(tokensPath, cancellationToken);
        var diskTokens = JsonSerializer.Deserialize<PlaudTokens>(diskJson)
            ?? throw new InvalidOperationException("Failed to deserialize Plaud tokens from disk.");

        var newTokens = await _refreshClient.RefreshAsync(diskTokens.RefreshToken, cancellationToken);

        if (string.IsNullOrEmpty(newTokens.AccessToken) || string.IsNullOrEmpty(newTokens.RefreshToken))
            throw new InvalidOperationException(
                "Plaud refresh response has empty tokens. Refusing to overwrite Key Vault.");

        if (newTokens.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new InvalidOperationException(
                $"Plaud refresh response has expires_at={newTokens.ExpiresAt} in the past. Refusing to overwrite Key Vault.");

        var newJson = JsonSerializer.Serialize(newTokens);

        // Write disk first — if KV fails, the running process still has the fresh token.
        await File.WriteAllTextAsync(tokensPath, newJson, cancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tokensPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await _secretClient.SetSecretAsync("Plaud--TokensJson", newJson, cancellationToken);

        _logger.LogInformation(
            "Plaud token refreshed. expires_at={ExpiresAt}", newTokens.ExpiresAt);
    }
}
