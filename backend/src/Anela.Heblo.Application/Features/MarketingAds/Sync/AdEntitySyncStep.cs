using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdEntitySyncResult(AdStreamOutcome Outcome, IReadOnlyDictionary<AdEntityKey, long> EntityIds);

/// <summary>
/// Entities stream for one account, shared by both jobs. For sources without a change log it
/// also writes snapshot-diff events — both jobs must, or the daily upsert would silently absorb
/// a change the hourly run had not seen yet. The entity upsert and the event write are staged and
/// saved with one SaveChanges (EF's implicit transaction): if the write fails, the upsert rolls back,
/// so the next run diffs the same change again. "Created" events are written only once the account
/// has had a successful entity sync, so the first run does not flood the table.
/// </summary>
public sealed class AdEntitySyncStep
{
    private static readonly IReadOnlyDictionary<AdEntityKey, long> NoIds = new Dictionary<AdEntityKey, long>();

    private readonly AdsDbContext _db;
    private readonly AdEntityUpserter _upserter;
    private readonly AdChangeEventWriter _writer;
    private readonly IAdSyncStateStore _state;
    private readonly AdStreamRunner _runner;
    private readonly TimeProvider _time;

    public AdEntitySyncStep(AdsDbContext db, AdEntityUpserter upserter, AdChangeEventWriter writer, IAdSyncStateStore state, AdStreamRunner runner, TimeProvider time)
    {
        _db = db;
        _upserter = upserter;
        _writer = writer;
        _state = state;
        _runner = runner;
        _time = time;
    }

    public async Task<AdEntitySyncResult> RunAsync(IAdPlatformReadSource source, AdAccount account, bool recordSnapshotDiffs, CancellationToken ct)
    {
        var previous = await _state.GetAsync(source.Platform, account.ExternalId, AdSyncStreams.Entities, ct);
        var reportCreations = recordSnapshotDiffs && previous?.LastSuccessAt is not null;
        var ids = NoIds;

        var outcome = await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStreams.Entities, async token =>
        {
            var observedAt = _time.GetUtcNow();
            var snapshots = await source.GetEntitiesAsync(account.ExternalId, token);

            var written = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async attemptCt =>
            {
                // A retry must not see the failed attempt's tracked state (see PollyExecutionStrategy).
                _db.ChangeTracker.Clear();
                if (!recordSnapshotDiffs)
                {
                    var upserted = await _upserter.UpsertAsync(account.Id, snapshots, reportCreations, attemptCt);
                    ids = upserted.EntityIds;
                    return upserted.RowsWritten;
                }

                var staged = await _upserter.StageAsync(account.Id, snapshots, reportCreations, attemptCt);
                var rows = staged.RowsWritten;
                if (staged.Changes.Count > 0)
                {
                    var candidates = staged.Changes.Select(c => AdChangeEventCandidate.FromSnapshot(c, observedAt)).ToList();
                    rows += await _writer.StageAsync(source.Platform, account, AdChangeSource.SnapshotDiff, candidates, staged.StagedIds, attemptCt);
                }

                // One SaveChanges = one implicit transaction: entities and their events commit or roll back together.
                await _db.SaveChangesAsync(attemptCt);
                ids = staged.Saved().EntityIds;
                _db.ChangeTracker.Clear();
                return rows;
            }, token);
            return new AdStreamWork(written, null);
        }, ct);

        return new AdEntitySyncResult(outcome, outcome.Succeeded ? ids : NoIds);
    }
}
