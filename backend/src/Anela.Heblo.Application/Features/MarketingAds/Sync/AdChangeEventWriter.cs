using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Inserts change events once per (account, source, external id) — the table's unique key —
/// and stamps each with its origin from <see cref="AdChangeOriginMatcher"/>.
/// </summary>
public sealed class AdChangeEventWriter
{
    private readonly AdsDbContext _db;
    private readonly IAdExecutionLookup _lookup;
    private readonly AdChangeOriginMatcher _matcher;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;

    public AdChangeEventWriter(AdsDbContext db, IAdExecutionLookup lookup, AdChangeOriginMatcher matcher, TimeProvider time, IOptions<AdsSyncOptions> options)
    {
        _db = db;
        _lookup = lookup;
        _matcher = matcher;
        _time = time;
        _options = options.Value;
    }

    /// <returns>Rows inserted. Ids already stored for the same account + source, and repeats within the batch, are skipped.</returns>
    public async Task<int> WriteAsync(AdPlatform platform, AdAccount account, AdChangeSource source,
        IReadOnlyList<AdChangeEventCandidate> candidates, IReadOnlyDictionary<AdEntityKey, long> entityIds, CancellationToken ct)
    {
        var unique = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.ExternalEventId))
            .DistinctBy(c => c.ExternalEventId, StringComparer.Ordinal)
            .ToList();
        if (unique.Count == 0)
            return 0;

        var sourceValue = AdsDbValues.ToDb(source);
        var fresh = await DropStoredAsync(account.Id, sourceValue, unique, ct);
        if (fresh.Count == 0)
            return 0;

        var window = TimeSpan.FromMinutes(_options.OutOfBandMatchWindowMinutes);
        var executed = await _lookup.GetExecutedActionsAsync(platform, account.ExternalId,
            fresh.Min(c => c.OccurredAt) - window, fresh.Max(c => c.OccurredAt) + window, ct);
        var rules = new AdOriginMatchingRules(window);
        var uniqueByExternalId = entityIds
            .GroupBy(p => p.Key.ExternalId, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Value, StringComparer.Ordinal);

        var written = 0;
        foreach (var chunk in fresh.Chunk(_options.BatchSize))
        {
            foreach (var candidate in chunk)
            {
                var match = _matcher.Match(candidate, executed, rules);
                _db.ChangeEvents.Add(new AdChangeEvent
                {
                    AccountId = account.Id,
                    ExternalEventId = candidate.ExternalEventId,
                    OccurredAt = candidate.OccurredAt.ToUniversalTime(),
                    Actor = candidate.Actor,
                    ActorKind = AdsDbValues.ToDb(candidate.ActorKind),
                    EntityId = ResolveEntityId(candidate, entityIds, uniqueByExternalId),
                    EntityLevel = candidate.EntityLevel is { } level ? AdsDbValues.ToDb(level) : null,
                    EntityExternalRef = candidate.EntityExternalId,
                    ChangeType = candidate.ChangeType,
                    OldValueJson = candidate.OldValueJson,
                    NewValueJson = candidate.NewValueJson,
                    Source = AdsDbValues.ToDb(source),
                    Origin = AdsDbValues.ToDb(match.Origin),
                    MatchedExecutionId = match.MatchedExecutionId,
                    SyncedAt = _time.GetUtcNow(),
                });
            }
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            written += chunk.Length;
        }

        return written;
    }

    private async Task<List<AdChangeEventCandidate>> DropStoredAsync(long accountId, string sourceValue,
        List<AdChangeEventCandidate> unique, CancellationToken ct)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in unique.Select(c => c.ExternalEventId).Chunk(_options.BatchSize))
        {
            known.UnionWith(await _db.ChangeEvents.AsNoTracking()
                .Where(e => e.AccountId == accountId && e.Source == sourceValue && chunk.Contains(e.ExternalEventId))
                .Select(e => e.ExternalEventId)
                .ToListAsync(ct));
        }

        return unique.Where(c => !known.Contains(c.ExternalEventId)).ToList();
    }

    private static long? ResolveEntityId(AdChangeEventCandidate candidate, IReadOnlyDictionary<AdEntityKey, long> entityIds,
        IReadOnlyDictionary<string, long> uniqueByExternalId)
    {
        if (candidate.EntityExternalId is null)
            return null;
        if (candidate.EntityLevel is { } level)
            return entityIds.TryGetValue(new AdEntityKey(level, candidate.EntityExternalId), out var id) ? id : null;
        return uniqueByExternalId.TryGetValue(candidate.EntityExternalId, out var only) ? only : null;
    }
}
