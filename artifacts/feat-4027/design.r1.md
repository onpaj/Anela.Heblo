# Design: Targeted material-name lookup in GetConsumptionHistoryHandler

## Component Design

### `IPackingMaterialRepository` (Domain — `Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`)
Gains one new member, inserted directly after `GetRecentLogsForMaterialsAsync` (same "bulk lookup keyed by material id" family):

```csharp
/// <summary>
/// Resolves display names for a set of packing materials by id. Ids with no matching
/// material are simply absent from the result (no exception). When <paramref name="packingMaterialIds"/>
/// is empty, returns an empty dictionary without executing a database query.
/// </summary>
Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
    IEnumerable<int> packingMaterialIds,
    CancellationToken cancellationToken = default);
```

**Responsibility:** targeted `Id -> Name` resolution for an arbitrary, caller-supplied set of packing material ids. Does not replace `GetAllAsync`, which remains available for callers that genuinely need the full set (e.g. `GetPackingMaterialsListHandler`'s Part A).

**Contract:**
- Deduplicates internally if the caller passes duplicate ids (result is a dictionary, keyed by id).
- Empty input → empty output, **no DB round trip**.
- Ids with no matching row are silently absent from the result — never throws for an unresolvable id.
- Read-only; no tracked entities attached to the context.

### `PackingMaterialRepository` (Persistence — `Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`)
Implements the new method, placed near `GetRecentLogsForMaterialsAsync` for locality, mirroring that method's existing empty-collection short-circuit idiom exactly:

```csharp
public async Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
    IEnumerable<int> packingMaterialIds,
    CancellationToken cancellationToken = default)
{
    var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
    if (ids.Count == 0)
    {
        return new Dictionary<int, string>();
    }

    var rows = await DbSet
        .Where(m => ids.Contains(m.Id))
        .Select(m => new { m.Id, m.Name })
        .ToListAsync(cancellationToken);

    return rows.ToDictionary(r => r.Id, r => r.Name);
}
```

Uses the inherited `DbSet` field (consistent with this repository's convention for plain `PackingMaterials`-table queries), projects server-side to `Id`/`Name` only, and materializes with `.ToListAsync()` followed by an in-memory `.ToDictionary(...)` rather than `.ToDictionaryAsync(...)` on the anonymous projection (avoids an EF Core translation pitfall on some provider versions).

### `GetConsumptionHistoryHandler` (Application — `Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs`)
`Handle` call sequence changes from:

```
GetConsumptionHistoryAsync(...) -> (records, totalCount)
GetAllAsync(ct) -> ToDictionary(all materials)          // REMOVED
records.Select(r => MapToDto(r, materialNames))
```

to:

```
GetConsumptionHistoryAsync(...) -> (records, totalCount)
materialIds = records.Select(r => r.PackingMaterialId).Distinct()
GetMaterialNamesByIdsAsync(materialIds, ct) -> materialNames    // NEW
records.Select(r => MapToDto(r, materialNames))
```

`materialIds` is derived from the already-paginated `records` collection (in-memory `Distinct()`, no query), so the lookup is bounded by page size (≤ `MaxPageSize` = 100) rather than by total material count. `MapToDto`'s existing `"Neznámý"` fallback for a dictionary miss is unchanged — it is exercised the same way whether the miss comes from a truly-deleted material or (structurally impossible now, but previously theoretically possible) an id absent from a stale full-table snapshot.

No other method on the handler, and no controller/OpenAPI-facing type, changes.

### `MockPackingMaterialRepository` (Test double — `Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`)
Implements the new interface member by filtering its in-memory `_materials` list to the supplied ids, mirroring how `GetAllAsync` and `GetRecentLogsForMaterialsAsync` are already faked in this class. Required simply to keep the class a complete `IPackingMaterialRepository` implementation — existing `Handle_*` tests in `GetConsumptionHistoryHandlerTests.cs` continue to pass through it unchanged.

### `GetConsumptionHistoryQueryCountTests` (new test file, Test — `Anela.Heblo.Tests/Features/PackingMaterials/`)
New file, sibling to `PackingMaterialsListQueryCountTests.cs`, not an addition to it (that file is scoped and documented as covering `GetPackingMaterialsListHandler` specifically). Contains its own file-local `CountingRepositoryWrapper`: a full `IPackingMaterialRepository` pass-through around a real `PackingMaterialRepository` backed by an EF Core in-memory `ApplicationDbContext`, counting invocations of `GetAllAsync` and `GetMaterialNamesByIdsAsync`. Asserts:
- `GetAllAsync` called zero times by `GetConsumptionHistoryHandler.Handle`.
- `GetMaterialNamesByIdsAsync` called exactly once.
- The ids passed to `GetMaterialNamesByIdsAsync` are a subset of, and no larger than, the distinct `PackingMaterialId` values present in the returned page.

## Data Schemas

No schema, entity, or migration changes. `PackingMaterials` remains the same table; the new repository method reads it with a narrower `WHERE Id IN (...)` filter instead of an unconditional scan.

### New repository method signature

```csharp
Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
    IEnumerable<int> packingMaterialIds,
    CancellationToken cancellationToken = default);
```

| Input | Output |
|---|---|
| `packingMaterialIds`: any `IEnumerable<int>`, possibly empty or containing duplicates | `IReadOnlyDictionary<int, string>` keyed by `Id`, deduplicated, containing only ids that resolved to a row |
| Empty collection | Empty dictionary, no DB query issued |
| Ids with no matching `PackingMaterial` row | Simply absent from the result dictionary — no exception |

### Generated SQL shape (EF Core translation)

```sql
SELECT "m"."Id", "m"."Name"
FROM "PackingMaterials" AS "m"
WHERE "m"."Id" = ANY(@ids)   -- or equivalent IN (...) form
```

Two scalar columns only — no other `PackingMaterial` columns are selected, and no entity is tracked.

### Unchanged shapes
- `MaterialConsumptionHistoryRecord` (domain, plain class) — unchanged.
- `GetConsumptionHistoryRequest` / `GetConsumptionHistoryResponse` / `MaterialConsumptionHistoryItemDto` (application/API DTOs, classes per project convention) — unchanged. No new or removed fields, no OpenAPI client regeneration required.
- Round-trip count per `Handle` invocation stays at exactly two: one for `GetConsumptionHistoryAsync`, one for name resolution. Only the cost profile of the second changes, from `O(total packing materials)` to `O(≤ 100 distinct ids)`.
