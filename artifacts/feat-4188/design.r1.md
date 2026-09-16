# Design: Photobank Batch Tag Upsert N+1 Query Fix

## Component Design

### `IPhotobankPhotoTagRepository` (interface) — `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs`
Add one new method, additive to the existing interface:
```csharp
Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(
    IReadOnlyCollection<int> photoIds,
    PhotoTagSource source,
    CancellationToken cancellationToken);
```
- **Responsibility:** load, in one query, all `PhotoTag` rows for the given `photoIds` matching `source`, grouped by `PhotoId`.
- Returns an empty dictionary (not `null`, no per-key default) when `photoIds` is empty or no rows match; callers use `TryGetValue` and fall back to an empty list.
- Existing singular `GetPhotoTagsByPhotoAndSourceAsync(int photoId, ...)` is left untouched (no other call sites; kept for interface stability / any future single-photo use).

`GetOccupiedTagPairsAsync(string? scopeToTagName, ...)` keeps its existing signature and continues to return `HashSet<(int PhotoId, int TagId)>`; the developer scopes its use to the current batch's photo IDs by filtering the returned set to those IDs after the call, or — if profiling of the current `PhotoTags` row count shows the unscoped scan is already too costly — adds a photo-ID-scoped overload following the same `(IReadOnlyCollection<int> photoIds, ...)` shape as the new method above. Either choice is implementation detail; the contract this design fixes is: **the tag-reconciliation phase issues a bounded, batch-size-independent number of queries**, not the exact SQL shape of that one call.

### `PhotobankPhotoTagRepository` (implementation) — `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs`
```csharp
public async Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(
    IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken)
{
    if (photoIds.Count == 0)
        return new Dictionary<int, List<PhotoTag>>();

    var rows = await _context.PhotoTags
        .Where(pt => photoIds.Contains(pt.PhotoId) && pt.Source == source)
        .ToListAsync(cancellationToken);

    return rows
        .GroupBy(pt => pt.PhotoId)
        .ToDictionary(g => g.Key, g => g.ToList());
}
```
Follows the exact pattern already used by `RemovePhotoTagsBySourceAsync` (same file, lines 83–89) for photo-ID-scoped, source-filtered bulk operations — no new query style introduced.

### `PhotobankIndexJob.UpsertPhotoBatchAsync` — `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`
Replace the body of the per-photo loop (current lines 240–259) as follows. Everything above line 240 (Phase A, `itemMatches`, `allMatchingTagNames`, `tagIdsByName`, `tagNamesByPhoto`) is unchanged.

```csharp
var batchPhotoIds = tagNamesByPhoto.Keys.Select(p => p.Id).ToList();

var existingRuleTagsByPhoto = batchPhotoIds.Count > 0
    ? await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(batchPhotoIds, PhotoTagSource.Rule, ct)
    : new Dictionary<int, List<PhotoTag>>();

var occupiedNonRulePairs = batchPhotoIds.Count > 0
    ? await _photoTagRepository.GetOccupiedTagPairsAsync(scopeToTagName: null, ct)
    : new HashSet<(int PhotoId, int TagId)>();
// If GetOccupiedTagPairsAsync is extended with photo-ID scoping (see design note above),
// pass batchPhotoIds here instead of relying on the unscoped call.

var addedPairsThisBatch = new HashSet<(int PhotoId, int TagId)>();

foreach (var (photo, tagNames) in tagNamesByPhoto)
{
    var existingRuleTags = existingRuleTagsByPhoto.TryGetValue(photo.Id, out var tags)
        ? tags
        : new List<PhotoTag>();
    await _photoTagRepository.RemovePhotoTagsAsync(existingRuleTags, ct);

    foreach (var tagName in tagNames)
    {
        if (!tagIdsByName.TryGetValue(tagName, out var tagId)) continue;

        var pair = (photo.Id, tagId);
        if (occupiedNonRulePairs.Contains(pair)) continue;
        if (!addedPairsThisBatch.Add(pair)) continue;

        await _photoTagRepository.AddPhotoTagAsync(new PhotoTag
        {
            PhotoId = photo.Id,
            TagId = tagId,
            Source = PhotoTagSource.Rule,
            CreatedAt = DateTime.UtcNow,
        }, ct);
    }
}

await _photoTagRepository.SaveChangesAsync(ct);
```

Key points carried over from the architecture review:
- `occupiedNonRulePairs` mirrors `ReapplyRulesHandler`'s use of `GetOccupiedTagPairsAsync` — it deliberately excludes Rule-source rows, since those are the ones being removed-and-reapplied this same pass, not ones to guard against.
- `addedPairsThisBatch` is the new in-memory guard against inserting the same `(PhotoId, TagId)` pair twice within one batch — the responsibility the old per-photo `PhotoTagExistsAsync` DB check *incidentally* served (per the architecture review's finding), now made explicit and correct instead of implicit and DB-round-trip-dependent.
- Total query count for the tag-reconciliation phase is now fixed at 2 per batch (`GetPhotoTagsByPhotosAndSourceAsync` + `GetOccupiedTagPairsAsync`), independent of batch size or rule count, plus the single closing `SaveChangesAsync`.

## Data Schemas
No schema changes. Existing `PhotoTag` entity (composite key `(PhotoId, TagId)`, `Source` discriminator) is read and written exactly as today — only the query pattern around it changes:

| Repository call (before) | Cardinality | Repository call (after) | Cardinality |
|---|---|---|---|
| `GetPhotoTagsByPhotoAndSourceAsync(photoId, Rule, ct)` | 1 per photo | `GetPhotoTagsByPhotosAndSourceAsync(batchPhotoIds, Rule, ct)` | 1 per batch |
| `PhotoTagExistsAsync(photoId, tagId, ct)` | 1 per (photo, matched tag) | `GetOccupiedTagPairsAsync(null, ct)` + in-memory `HashSet` lookups | 1 per batch |
| `RemovePhotoTagsAsync` / `AddPhotoTagAsync` | unchanged (in-memory `DbContext` mutations, no round-trip) | unchanged | unchanged |
| `SaveChangesAsync` | 1 per batch (unchanged) | 1 per batch (unchanged) | unchanged |

No new API endpoints, events, or external payload shapes — this is entirely internal to the nightly job and its repository dependency.
