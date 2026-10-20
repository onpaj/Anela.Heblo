using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdObservedChange(AdEntityKey Key, long EntityId, AdFieldChange Change);

public sealed record AdEntityUpsertResult(
    IReadOnlyDictionary<AdEntityKey, long> EntityIds,
    IReadOnlyList<AdObservedChange> Changes,
    int RowsWritten);

/// <summary>
/// An upsert tracked but not yet saved. <see cref="StagedIds"/> and the ids in <see cref="Changes"/> may be EF
/// temporary keys — valid only for foreign keys set before the save. Call <see cref="Saved"/> after SaveChanges
/// for the stored ids.
/// </summary>
public sealed class AdEntityStagedUpsert
{
    private readonly IReadOnlyDictionary<AdEntityKey, AdEntity> _rows;
    private readonly IReadOnlyList<(AdEntityKey Key, AdEntity Row, AdFieldChange Change)> _pending;

    internal AdEntityStagedUpsert(IReadOnlyDictionary<AdEntityKey, long> stagedIds, IReadOnlyList<AdObservedChange> changes, int rowsWritten,
        IReadOnlyDictionary<AdEntityKey, AdEntity> rows, IReadOnlyList<(AdEntityKey Key, AdEntity Row, AdFieldChange Change)> pending)
    {
        StagedIds = stagedIds;
        Changes = changes;
        RowsWritten = rowsWritten;
        _rows = rows;
        _pending = pending;
    }

    public IReadOnlyDictionary<AdEntityKey, long> StagedIds { get; }
    public IReadOnlyList<AdObservedChange> Changes { get; }
    public int RowsWritten { get; }

    /// <summary>The result with stored ids. Only meaningful once the staged rows have been saved.</summary>
    public AdEntityUpsertResult Saved() => new(
        _rows.ToDictionary(p => p.Key, p => p.Value.Id),
        _pending.Select(p => new AdObservedChange(p.Key, p.Row.Id, p.Change)).ToList(),
        RowsWritten);
}

/// <summary>
/// Upserts ad_entities by (account, level, external id) and diffs name/status/attributes against
/// the stored row — the snapshot-diff fallback's source of truth.
/// </summary>
public sealed class AdEntityUpserter
{
    /// <summary>last_seen_at is only bumped this often for unchanged rows, so an hourly run does not rewrite every row.</summary>
    public static readonly TimeSpan LastSeenRefreshInterval = TimeSpan.FromHours(12);

    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;

    public AdEntityUpserter(AdsDbContext db, TimeProvider time, IOptions<AdsSyncOptions> options)
    {
        _db = db;
        _time = time;
        _options = options.Value;
    }

    public async Task<IReadOnlyDictionary<AdEntityKey, long>> LoadIdsAsync(long accountId, CancellationToken ct)
    {
        var rows = await _db.Entities.AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .Select(e => new { e.Level, e.ExternalId, e.Id })
            .ToListAsync(ct);
        return rows.ToDictionary(r => new AdEntityKey(AdsDbValues.FromDb<AdEntityLevel>(r.Level), r.ExternalId), r => r.Id);
    }

    /// <summary>
    /// Upserts and saves, flushing every <see cref="AdsSyncOptions.BatchSize"/> pending rows. Not atomic across batches.
    /// </summary>
    public async Task<AdEntityUpsertResult> UpsertAsync(long accountId, IReadOnlyList<AdEntitySnapshot> snapshots, bool reportCreations, CancellationToken ct)
    {
        var staged = await StageCoreAsync(accountId, snapshots, reportCreations, flushEvery: _options.BatchSize, ct);
        await _db.SaveChangesAsync(ct);
        var result = staged.Saved();
        _db.ChangeTracker.Clear();
        return result;
    }

    /// <summary>
    /// Tracks every insert/update (parents included) without saving, so the caller can add dependent rows and
    /// commit all of it with one SaveChanges — EF's implicit transaction makes the whole write atomic. New rows
    /// carry EF temporary keys until then; <see cref="AdEntityStagedUpsert.StagedIds"/> and the change ids use them,
    /// and EF fixes up every foreign key set to one when the row is inserted.
    /// </summary>
    public Task<AdEntityStagedUpsert> StageAsync(long accountId, IReadOnlyList<AdEntitySnapshot> snapshots, bool reportCreations, CancellationToken ct) =>
        StageCoreAsync(accountId, snapshots, reportCreations, flushEvery: null, ct);

    private async Task<AdEntityStagedUpsert> StageCoreAsync(long accountId, IReadOnlyList<AdEntitySnapshot> snapshots, bool reportCreations,
        int? flushEvery, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var existing = (await _db.Entities.Where(e => e.AccountId == accountId).ToListAsync(ct))
            .ToDictionary(e => new AdEntityKey(AdsDbValues.FromDb<AdEntityLevel>(e.Level), e.ExternalId));
        var unique = snapshots
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalId))
            .DistinctBy(s => new AdEntityKey(s.Level, s.ExternalId))
            .ToList();
        var pending = new List<(AdEntityKey Key, AdEntity Row, AdFieldChange Change)>();
        var written = 0;
        var pendingSinceFlush = 0;

        foreach (var snapshot in unique)
        {
            var key = new AdEntityKey(snapshot.Level, snapshot.ExternalId);
            var after = new AdEntityState(snapshot.Name, snapshot.Status, snapshot.Attributes);

            if (existing.TryGetValue(key, out var row))
            {
                var before = new AdEntityState(row.Name, AdsDbValues.FromDb<AdEntityStatus>(row.Status), AdAttributesJson.Parse(row.AttributesJson));
                var diff = AdSnapshotDiffer.Diff(before, after);
                if (diff.Count > 0)
                {
                    Apply(row, snapshot, now);
                    pending.AddRange(diff.Select(change => (key, row, change)));
                    written++;
                    pendingSinceFlush++;
                }
                else if (now - row.LastSeenAt >= LastSeenRefreshInterval)
                {
                    row.LastSeenAt = now;
                    pendingSinceFlush++;
                }
            }
            else
            {
                row = new AdEntity
                {
                    AccountId = accountId,
                    Level = AdsDbValues.ToDb(snapshot.Level),
                    ExternalId = snapshot.ExternalId,
                    FirstSeenAt = now,
                };
                Apply(row, snapshot, now);
                _db.Entities.Add(row);
                existing[key] = row;
                written++;
                pendingSinceFlush++;
                if (reportCreations)
                    pending.Add((key, row, AdSnapshotDiffer.Created(after)));
            }

            if (flushEvery is { } batchSize && pendingSinceFlush >= batchSize)
            {
                await _db.SaveChangesAsync(ct);
                pendingSinceFlush = 0;
            }
        }

        written += AssignParents(unique, existing, KeyOf);
        var stagedIds = existing.ToDictionary(p => p.Key, p => KeyOf(p.Value));
        var changes = pending.Select(p => new AdObservedChange(p.Key, KeyOf(p.Row), p.Change)).ToList();
        return new AdEntityStagedUpsert(stagedIds, changes, written, existing, pending);
    }

    /// <summary>The row's key as EF tracks it: the stored id, or a temporary value while the row is still unsaved.</summary>
    private long KeyOf(AdEntity row) => _db.Entry(row).Property(e => e.Id).CurrentValue;

    private static void Apply(AdEntity row, AdEntitySnapshot snapshot, DateTimeOffset now)
    {
        row.Name = snapshot.Name;
        row.Status = AdsDbValues.ToDb(snapshot.Status);
        row.AttributesJson = AdAttributesJson.Serialize(snapshot.Attributes);
        row.LastSeenAt = now;
        row.UpdatedAt = now;
    }

    private static int AssignParents(IEnumerable<AdEntitySnapshot> snapshots, IReadOnlyDictionary<AdEntityKey, AdEntity> rows,
        Func<AdEntity, long> keyOf)
    {
        var changed = 0;
        foreach (var snapshot in snapshots)
        {
            var row = rows[new AdEntityKey(snapshot.Level, snapshot.ExternalId)];
            long? parentId = snapshot.ParentLevel is { } level && snapshot.ParentExternalId is { } parent
                && rows.TryGetValue(new AdEntityKey(level, parent), out var parentRow)
                    ? keyOf(parentRow)
                    : null;
            if (row.ParentId != parentId)
            {
                row.ParentId = parentId;
                changed++;
            }
        }
        return changed;
    }
}
