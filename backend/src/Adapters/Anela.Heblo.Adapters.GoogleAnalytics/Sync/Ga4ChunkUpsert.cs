using Anela.Heblo.Persistence.Ga4;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// Replaces one date chunk's rows with what GA4 just returned.
///
/// It is a replace, not an append: GA4 revises recent days, and a landing page can legitimately
/// drop out of the day's top N. A row that survived in the table but not in the new report is
/// stale, so it is deleted. Deletion goes through RemoveRange rather than ExecuteDelete because
/// the EF InMemory provider — which the tests use — throws on ExecuteDelete.
/// </summary>
internal static class Ga4ChunkUpsert
{
    public static async Task<int> ReplaceRangeAsync<TEntity, TKey>(
        Ga4DbContext dbContext,
        DbSet<TEntity> set,
        IReadOnlyList<TEntity> incoming,
        IReadOnlyList<TEntity> existing,
        Func<TEntity, TKey> keyOf,
        Action<TEntity, TEntity> applyTo,
        int batchSize,
        CancellationToken ct)
        where TEntity : class
        where TKey : notnull
    {
        // GA4 returning nothing for a window that already holds rows is never a legitimate
        // "everything was deleted" signal — GA4 does not retract history. It means the report
        // failed to produce data: credentials repointed at the wrong property, an outage, or an
        // over-restrictive path filter. Deleting on it would hollow out a rolling
        // TrailingReprocessDays+1 window every night, mark the run OK and advance the watermark,
        // with no operator signal at all. Fail the chunk instead, so the watermark holds.
        if (incoming.Count == 0 && existing.Count > 0)
        {
            throw new InvalidOperationException(
                $"GA4 returned no rows for a window that already holds {existing.Count} stored row(s). " +
                "Refusing to delete them — check the property id, credentials and any path filter.");
        }

        var existingByKey = new Dictionary<TKey, TEntity>();
        foreach (var row in existing)
            existingByKey[keyOf(row)] = row;

        var seen = new HashSet<TKey>();
        var pending = 0;

        foreach (var row in incoming)
        {
            var key = keyOf(row);
            if (!seen.Add(key))
                continue;

            if (existingByKey.TryGetValue(key, out var current))
            {
                applyTo(current, row);
            }
            else
            {
                set.Add(row);
            }

            if (++pending >= batchSize)
            {
                await dbContext.SaveChangesAsync(ct);
                pending = 0;
            }
        }

        // Deletions go through the same batch budget as the writes. Lower a top-N cap, or hit a
        // chunk whose rows have all aged out, and this can be thousands of rows — one
        // SaveChangesAsync for all of them is exactly the long transaction BatchSize exists to
        // keep off the shared single-vCore server.
        foreach (var row in existing)
        {
            if (seen.Contains(keyOf(row)))
                continue;

            set.Remove(row);

            if (++pending >= batchSize)
            {
                await dbContext.SaveChangesAsync(ct);
                pending = 0;
            }
        }

        if (pending > 0)
            await dbContext.SaveChangesAsync(ct);

        return seen.Count;
    }
}
