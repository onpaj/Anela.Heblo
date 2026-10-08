using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdEntitySyncResult(AdStreamOutcome Outcome, IReadOnlyDictionary<AdEntityKey, long> EntityIds);

/// <summary>
/// Entities stream for one account, shared by both jobs. For sources without a change log it
/// also writes snapshot-diff events — both jobs must, or the daily upsert would silently absorb
/// a change the hourly run had not seen yet. "Created" events are written only once the account
/// has had a successful entity sync, so the first run does not flood the table.
/// </summary>
public sealed class AdEntitySyncStep
{
    private static readonly IReadOnlyDictionary<AdEntityKey, long> NoIds = new Dictionary<AdEntityKey, long>();

    private readonly AdEntityUpserter _upserter;
    private readonly AdChangeEventWriter _writer;
    private readonly IAdSyncStateStore _state;
    private readonly AdStreamRunner _runner;
    private readonly TimeProvider _time;

    public AdEntitySyncStep(AdEntityUpserter upserter, AdChangeEventWriter writer, IAdSyncStateStore state, AdStreamRunner runner, TimeProvider time)
    {
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
            var result = await _upserter.UpsertAsync(account.Id, snapshots, reportCreations, token);
            ids = result.EntityIds;

            var written = result.RowsWritten;
            if (recordSnapshotDiffs && result.Changes.Count > 0)
            {
                var candidates = result.Changes.Select(c => AdChangeEventCandidate.FromSnapshot(c, observedAt)).ToList();
                written += await _writer.WriteAsync(source.Platform, account, AdChangeSource.SnapshotDiff, candidates, ids, token);
            }
            return new AdStreamWork(written, null);
        }, ct);

        return new AdEntitySyncResult(outcome, outcome.Succeeded ? ids : NoIds);
    }
}
