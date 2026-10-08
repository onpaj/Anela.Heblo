using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Result of one stream's work. A null watermark keeps the stored one.</summary>
public sealed record AdStreamWork(int RowsWritten, DateTimeOffset? Watermark);

public sealed record AdStreamOutcome(bool Succeeded, int RowsWritten, string? Error)
{
    public static AdStreamOutcome Ok(int rowsWritten) => new(true, rowsWritten, null);
    public static AdStreamOutcome Failed(string error) => new(false, 0, error);
}

/// <summary>
/// Runs one platform x account x stream unit: isolates its failure, clears the shared
/// DbContext's change tracker so a failed save cannot poison the next stream, and writes sync_state.
/// Cancellation is checked on the token, not by exception type: an HTTP timeout is also an
/// OperationCanceledException and must count as a stream failure.
/// </summary>
public sealed class AdStreamRunner
{
    private readonly AdsDbContext _db;
    private readonly IAdSyncStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger<AdStreamRunner> _logger;

    public AdStreamRunner(AdsDbContext db, IAdSyncStateStore state, TimeProvider time, ILogger<AdStreamRunner> logger)
    {
        _db = db;
        _state = state;
        _time = time;
        _logger = logger;
    }

    /// <param name="stream">One of <see cref="Anela.Heblo.Persistence.Ads.Entities.AdSyncStreams"/>.</param>
    public async Task<AdStreamOutcome> RunAsync(AdPlatform platform, string accountExternalId, string stream,
        Func<CancellationToken, Task<AdStreamWork>> work, CancellationToken ct)
    {
        try
        {
            var result = await work(ct);
            await _state.RecordSuccessAsync(platform, accountExternalId, stream, _time.GetUtcNow(), result.Watermark, ct);
            _logger.LogInformation("AdsSync.StreamCompleted {Platform} {Account} {Stream} rows={Rows}",
                platform, accountExternalId, stream, result.RowsWritten);
            return AdStreamOutcome.Ok(result.RowsWritten);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "AdsSync.StreamFailed {Platform} {Account} {Stream}", platform, accountExternalId, stream);
            await TryRecordFailureAsync(platform, accountExternalId, stream, ex.Message, ct);
            return AdStreamOutcome.Failed($"{platform}/{accountExternalId}/{stream}: {ex.Message}");
        }
    }

    private async Task TryRecordFailureAsync(AdPlatform platform, string accountExternalId, string stream, string error, CancellationToken ct)
    {
        try
        {
            await _state.RecordFailureAsync(platform, accountExternalId, stream, error, ct);
        }
        catch (Exception stateEx) when (!ct.IsCancellationRequested)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(stateEx, "AdsSync.StateWriteFailed {Platform} {Account} {Stream}", platform, accountExternalId, stream);
        }
    }
}
