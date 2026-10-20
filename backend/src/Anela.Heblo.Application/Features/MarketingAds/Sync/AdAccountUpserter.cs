using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdManagedAccounts(IReadOnlyList<AdAccount> Accounts, string? Error);

public sealed class AdAccountUpserter
{
    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdAccountUpserter> _logger;

    public AdAccountUpserter(AdsDbContext db, TimeProvider time, IOptions<AdsSyncOptions> options, ILogger<AdAccountUpserter> logger)
    {
        _db = db;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Lists the source's accounts, upserts them, returns the managed ones. Never throws for a source failure.</summary>
    public async Task<AdManagedAccounts> SyncManagedAsync(IAdPlatformReadSource source, CancellationToken ct)
    {
        try
        {
            var snapshots = await source.GetAccountsAsync(ct);
            var rows = await UpsertAsync(source.Platform, snapshots, ct);
            return new AdManagedAccounts(rows.Where(a => a.IsManaged).ToList(), null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "AdsSync.AccountsFailed {Platform}", source.Platform);
            return new AdManagedAccounts(Array.Empty<AdAccount>(), $"{source.Platform}: accounts: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AdAccount>> UpsertAsync(AdPlatform platform, IReadOnlyList<AdAccountSnapshot> snapshots, CancellationToken ct)
    {
        var platformValue = AdsDbValues.ToDb(platform);
        var unique = snapshots.Where(s => !string.IsNullOrWhiteSpace(s.ExternalId)).DistinctBy(s => s.ExternalId).ToList();
        var ids = unique.Select(s => s.ExternalId).ToList();
        var existing = await _db.Accounts
            .Where(a => a.Platform == platformValue && ids.Contains(a.ExternalId))
            .ToDictionaryAsync(a => a.ExternalId, ct);
        var managedKeys = _options.ManagedAccountKeys();
        var now = _time.GetUtcNow();

        foreach (var snapshot in unique)
        {
            if (existing.TryGetValue(snapshot.ExternalId, out var row))
            {
                // is_managed is never overwritten for a known account: an admin may have changed it.
                row.Name = snapshot.Name;
                row.Currency = snapshot.Currency;
                row.TimeZone = snapshot.TimeZone;
                row.UpdatedAt = now;
                continue;
            }

            row = new AdAccount
            {
                Platform = platformValue,
                ExternalId = snapshot.ExternalId,
                Name = snapshot.Name,
                Currency = snapshot.Currency,
                TimeZone = snapshot.TimeZone,
                IsManaged = managedKeys.Count == 0 || managedKeys.Contains($"{platform}:{snapshot.ExternalId}"),
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Accounts.Add(row);
            existing[snapshot.ExternalId] = row;
        }

        await _db.SaveChangesAsync(ct);
        var result = existing.Values.ToList();
        _db.ChangeTracker.Clear();
        return result;
    }
}
