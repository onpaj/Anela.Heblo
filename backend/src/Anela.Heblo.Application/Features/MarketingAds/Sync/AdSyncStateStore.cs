using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdSyncStateSnapshot(DateTimeOffset? Watermark, string Status, DateTimeOffset? LastSuccessAt, string? LastError);

public interface IAdSyncStateStore
{
    Task<AdSyncStateSnapshot?> GetAsync(AdPlatform platform, string accountExternalId, string stream, CancellationToken ct);
    Task RecordSuccessAsync(AdPlatform platform, string accountExternalId, string stream, DateTimeOffset at, DateTimeOffset? watermark, CancellationToken ct);
    Task RecordFailureAsync(AdPlatform platform, string accountExternalId, string stream, string error, CancellationToken ct);
}

/// <summary>ads.sync_state, one row per platform x account x stream. Never uses FindAsync (it aliases tracked rows).</summary>
public sealed class AdSyncStateStore : IAdSyncStateStore
{
    public const int MaxErrorLength = 2000;
    private readonly AdsDbContext _db;

    private readonly TimeProvider _time;

    public AdSyncStateStore(AdsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<AdSyncStateSnapshot?> GetAsync(AdPlatform platform, string accountExternalId, string stream, CancellationToken ct)
    {
        var (p, s) = (AdsDbValues.ToDb(platform), stream);
        var row = await _db.SyncStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Platform == p && x.AccountExternalId == accountExternalId && x.Stream == s, ct);
        return row is null ? null : new AdSyncStateSnapshot(row.Watermark, row.Status ?? "", row.LastSuccessAt, row.LastError);
    }

    public async Task RecordSuccessAsync(AdPlatform platform, string accountExternalId, string stream, DateTimeOffset at, DateTimeOffset? watermark, CancellationToken ct)
    {
        var row = await LoadOrAddAsync(platform, accountExternalId, stream, ct);
        row.Status = AdsDbValues.StatusSucceeded;
        row.LastSuccessAt = at.ToUniversalTime();
        row.LastError = null;
        if (watermark is { } w)
            row.Watermark = w.ToUniversalTime();
        await SaveAsync(ct);
    }

    public async Task RecordFailureAsync(AdPlatform platform, string accountExternalId, string stream, string error, CancellationToken ct)
    {
        var row = await LoadOrAddAsync(platform, accountExternalId, stream, ct);
        row.Status = AdsDbValues.StatusFailed;
        row.LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        await SaveAsync(ct);
    }

    private async Task<AdSyncState> LoadOrAddAsync(AdPlatform platform, string accountExternalId, string stream, CancellationToken ct)
    {
        // A previous failed save may have left entities tracked; never let them ride along.
        _db.ChangeTracker.Clear();
        var (p, s) = (AdsDbValues.ToDb(platform), stream);
        var row = await _db.SyncStates.FirstOrDefaultAsync(x => x.Platform == p && x.AccountExternalId == accountExternalId && x.Stream == s, ct);
        if (row is null)
        {
            row = new AdSyncState { Platform = p, AccountExternalId = accountExternalId, Stream = s };
            _db.SyncStates.Add(row);
        }
        row.UpdatedAt = _time.GetUtcNow();
        return row;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }
}
