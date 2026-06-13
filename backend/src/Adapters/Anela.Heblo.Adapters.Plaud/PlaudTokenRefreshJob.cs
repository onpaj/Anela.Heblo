using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Plaud;

public class PlaudTokenRefreshJob : IRecurringJob
{
    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly IPlaudTokenStore _tokenStore;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<PlaudTokenRefreshJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "plaud-token-refresh",
        DisplayName = "Plaud — refresh auth token",
        Description = "Calls Plaud OAuth refresh endpoint weekly and persists the rotated token back to Key Vault so container restarts pick up the fresh value.",
        CronExpression = "0 4 * * 0",
        DefaultIsEnabled = true
    };

    public PlaudTokenRefreshJob(
        IPlaudTokenRefreshClient refreshClient,
        IPlaudTokenStore tokenStore,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PlaudTokenRefreshJob> logger)
    {
        _refreshClient = refreshClient;
        _tokenStore = tokenStore;
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

        var current = await _tokenStore.LoadAsync(cancellationToken);
        var newTokens = await _refreshClient.RefreshAsync(current.RefreshToken, cancellationToken);

        if (string.IsNullOrEmpty(newTokens.AccessToken) || string.IsNullOrEmpty(newTokens.RefreshToken))
            throw new InvalidOperationException(
                "Plaud refresh response has empty tokens. Refusing to persist.");

        if (newTokens.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new InvalidOperationException(
                $"Plaud refresh response has expires_at={newTokens.ExpiresAt} in the past. Refusing to persist.");

        var saveResult = await _tokenStore.SaveAsync(newTokens, cancellationToken);

        if (saveResult.KeyVaultWriteFailed)
            _logger.LogWarning(saveResult.KeyVaultError,
                "Plaud token KV write failed in weekly job (disk OK)");

        _logger.LogInformation(
            "Plaud token refreshed by weekly job. expires_at={ExpiresAt}", newTokens.ExpiresAt);
    }
}
