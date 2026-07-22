## Module
Photobank

## Finding
`PhotobankIndexJob.UpsertPhotoAsync` calls `SaveChangesAsync` **twice** for every item returned by the Graph delta API: once to flush the photo entity upsert (to obtain the DB-assigned `Photo.Id`), and once to flush the associated `PhotoTag` rows. This makes the job O(N) in database round-trips for N delta items.

File: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`

```csharp
// UpsertPhotoAsync — called for every item in the delta:
photo.FileName = item.Name;
// ...
await _repo.SaveChangesAsync(ct);  // line 138 — flush photo entity to get Id

// ... apply rule tags ...
await _repo.SaveChangesAsync(ct);  // line 159 — flush PhotoTag rows
```

The outer loop in `ExecuteAsync` (line 78: `foreach (var item in delta.Items)`) calls `UpsertPhotoAsync` for each item, so N delta items → 2N transactions.

## Why it matters
For the nightly differential sync this is usually small (tens of items). For an initial index of a large SharePoint library — or a re-index after root configuration change — the delta can contain tens of thousands of items. 2N sequential `SaveChanges` round-trips scale poorly and can make the job take many minutes where it should take seconds.

Additionally, the per-item `GetPhotoBySharePointFileIdAsync` (line 113) adds another N queries, but the `SaveChanges` pair dominates.

## Suggested fix
Process delta items in micro-batches within `UpsertPhotoAsync`'s caller:

1. Accumulate photo entity upserts across a batch of items.
2. Call `SaveChangesAsync` once to get all IDs assigned.
3. Resolve rule tags for the whole batch.
4. Bulk-insert `PhotoTag` rows.
5. Call `SaveChangesAsync` once.

A batch size of 100–500 items would reduce 2N transactions to approximately `2*(N/batchSize)` — typically 1–2 round-trips for a normal daily delta and a few dozen for an initial sync.

---
_Filed by daily arch-review routine on 2026-07-18._
