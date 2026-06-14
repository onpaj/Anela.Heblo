using System.Security.Cryptography;
using System.Text;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Plaud;

internal sealed class PlaudTokenManager : IPlaudTokenManager
{
    private readonly IPlaudTokenStore _store;
    private readonly IPlaudTokenRefreshClient _refreshClient;
    private readonly ITelemetryService _telemetry;
    private readonly IOptions<PlaudCredentialsOptions> _options;
    private readonly ILogger<PlaudTokenManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);

    private PlaudTokens? _cached;
    private bool _loaded;

    public PlaudTokenManager(
        IPlaudTokenStore store,
        IPlaudTokenRefreshClient refreshClient,
        ITelemetryService telemetry,
        IOptions<PlaudCredentialsOptions> options,
        ILogger<PlaudTokenManager> logger)
    {
        _store = store;
        _refreshClient = refreshClient;
        _telemetry = telemetry;
        _options = options;
        _logger = logger;
    }

    public async Task EnsureFreshAsync(CancellationToken ct)
    {
        var tokens = await GetCachedAsync(ct);
        if (!IsInsideExpiryBuffer(tokens)) return;

        await RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger.NearExpiry, ct);
    }

    public Task<bool> ForceRefreshAsync(CancellationToken ct) =>
        RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger.AuthFailedRetry, ct);

    private async Task<PlaudTokens> GetCachedAsync(CancellationToken ct)
    {
        if (_loaded && _cached is not null) return _cached;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_loaded && _cached is not null) return _cached;
            _cached = await _store.LoadAsync(ct);
            _loaded = true;
            return _cached;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool IsInsideExpiryBuffer(PlaudTokens tokens)
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(tokens.ExpiresAt);
        return expiresAt - DateTimeOffset.UtcNow <= _options.Value.ExpiryBuffer;
    }

    private async Task<bool> RefreshUnderSemaphoreAsync(PlaudTokenRefreshTrigger trigger, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var current = _cached ?? await _store.LoadAsync(ct);

            // Re-check after acquiring semaphore — a concurrent caller may have already refreshed.
            // If the token is now outside the expiry buffer, short-circuit regardless of trigger type.
            if (!IsInsideExpiryBuffer(current))
                return true;

            var tokenIdShort = ComputeTokenIdShort(current.RefreshToken);

            if (trigger == PlaudTokenRefreshTrigger.NearExpiry)
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.NearExpiry,
                    new Dictionary<string, string>
                    {
                        ["expiresAt"] = current.ExpiresAt.ToString(),
                        ["bufferHours"] = _options.Value.ExpiryBuffer.TotalHours.ToString("0"),
                        ["tokenIdShort"] = tokenIdShort
                    });
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.Value.RefreshTimeout);

            PlaudTokens rotated;
            try
            {
                rotated = await _refreshClient.RefreshAsync(current.RefreshToken, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                EmitRefreshFailed("Timeout", tokenIdShort);
                _logger.LogError("Plaud token refresh timed out after {Timeout}", _options.Value.RefreshTimeout);
                return false;
            }
            catch (Exception ex)
            {
                EmitRefreshFailed("HttpError", tokenIdShort);
                _logger.LogError(ex, "Plaud token refresh failed");
                return false;
            }

            if (string.IsNullOrEmpty(rotated.AccessToken) || string.IsNullOrEmpty(rotated.RefreshToken))
            {
                EmitRefreshFailed("EmptyResponse", tokenIdShort);
                return false;
            }

            if (rotated.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                EmitRefreshFailed("ExpiredInResponse", tokenIdShort);
                return false;
            }

            PlaudTokenSaveResult saveResult;
            try
            {
                saveResult = await _store.SaveAsync(rotated, ct);
            }
            catch (Exception ex)
            {
                EmitRefreshFailed("DiskWriteFailed", tokenIdShort);
                _logger.LogError(ex, "Plaud token disk write failed; in-memory cache NOT updated");
                return false;
            }

            _cached = rotated;
            _loaded = true;

            if (saveResult.KeyVaultWriteFailed)
            {
                _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.KeyVaultWriteFailed,
                    new Dictionary<string, string>
                    {
                        ["tokenIdShort"] = ComputeTokenIdShort(rotated.RefreshToken)
                    });
            }

            _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.Refreshed,
                new Dictionary<string, string>
                {
                    ["expiresAt"] = rotated.ExpiresAt.ToString(),
                    ["tokenIdShort"] = ComputeTokenIdShort(rotated.RefreshToken),
                    ["triggeredBy"] = trigger == PlaudTokenRefreshTrigger.NearExpiry ? "near-expiry" : "auth-failed-retry"
                });

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void EmitRefreshFailed(string reason, string tokenIdShort) =>
        _telemetry.TrackBusinessEvent(PlaudTelemetryEventNames.RefreshFailed,
            new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["tokenIdShort"] = tokenIdShort
            });

    internal string ComputeTokenIdShort(string refreshToken)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes)[..4].ToLowerInvariant();
    }
}
