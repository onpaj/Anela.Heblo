# Specification: Targeted material-name lookup in GetConsumptionHistoryHandler

## Summary
`GetConsumptionHistoryHandler.Handle` currently loads **every** packing material row (`GetAllAsync`) on every paginated request just to resolve the material names for the current page. This spec replaces that full-table load with a targeted lookup scoped to the distinct material IDs actually present in the current page, by adding a new `GetMaterialNamesByIdsAsync` method to `IPackingMaterialRepository`. This is a repository/handler-internal correctness-of-scaling fix with no API, DTO, or UI-visible behavior change.

## Background
`GetConsumptionHistoryAsync` already returns a paginated, filtered set of history records (max 100 per page, per `MaxPageSize`). Immediately after, the handler calls `_repository.GetAllAsync(cancellationToken)` and builds an `Id -> Name` dictionary from **all** packing materials in the database, even though at most `pageSize` distinct material IDs can appear on the current page. This is a second unconditional full-table scan on every page load, and its cost scales with total material count rather than with the page contents — an anti-pattern flagged by the architecture-review routine (issue #4027) even though the current material volume keeps the practical impact low today. `GetPackingMaterialsListHandler` in the same module already exists as a precedent for the correct pattern: it takes ids from Part A's results and passes them into a targeted batch method (`GetRecentLogsForMaterialsAsync`) rather than loading everything, and has a dedicated query-count test guarding that behavior (`PackingMaterialsListQueryCountTests`). This fix brings `GetConsumptionHistoryHandler` in line with that existing convention.

## Functional Requirements

### FR-1: Add a targeted material-name lookup to `IPackingMaterialRepository`
Add a new method to `IPackingMaterialRepository`:

```csharp
Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
    IEnumerable<int> packingMaterialIds,
    CancellationToken cancellationToken = default);
```

Implement it in `PackingMaterialRepository` (`backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`) as a targeted query filtered by `WHERE Id IN (...)`, projecting only `Id` and `Name` (no need to materialize full `PackingMaterial` entities), e.g. via `DbSet.Where(m => ids.Contains(m.Id)).Select(m => new { m.Id, m.Name })`. Mirror the existing `GetRecentLogsForMaterialsAsync` convention on the same interface: when the input id collection is empty, return an empty dictionary immediately **without** issuing a database query.

**Acceptance criteria:**
- `IPackingMaterialRepository` exposes `GetMaterialNamesByIdsAsync(IEnumerable<int>, CancellationToken)` returning `Task<IReadOnlyDictionary<int, string>>`.
- The EF Core implementation issues a single `WHERE Id IN (...)` query scoped to the supplied ids; it never enumerates the full `PackingMaterial` table.
- Passing an empty (or duplicate-containing) id collection returns an empty (respectively deduplicated) dictionary without a database round trip for the empty case.
- Ids with no matching material are simply absent from the returned dictionary (no exception).

### FR-2: `GetConsumptionHistoryHandler` resolves names from the current page only
Replace the `GetAllAsync` call in `GetConsumptionHistoryHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs`, lines 48-49) with:

```csharp
var materialIds = records.Select(r => r.PackingMaterialId).Distinct();
var materialNames = await _repository.GetMaterialNamesByIdsAsync(materialIds, cancellationToken);
```

The distinct-id extraction must happen **after** `GetConsumptionHistoryAsync` returns the already-paginated `records`, so the lookup is bounded by the current page's content (at most `pageSize`, i.e. at most `MaxPageSize` = 100, distinct ids), not by total material count. Existing name-resolution behavior (`MapToDto` falling back to `"Neznámý"` for an id not present in the dictionary) is unchanged.

**Acceptance criteria:**
- The handler no longer calls `_repository.GetAllAsync` anywhere in the `Handle` method.
- The handler calls `GetMaterialNamesByIdsAsync` exactly once per `Handle` invocation, with the distinct set of `PackingMaterialId` values drawn from the returned `records` for that page (not from an unfiltered/unpaginated source).
- When `records` is empty (e.g., a page beyond `TotalCount`, or an empty result set), `GetMaterialNamesByIdsAsync` is called with an empty id collection (or the call is a no-op that returns an empty dictionary) — no full-table load occurs as a fallback.
- Response shape (`GetConsumptionHistoryResponse`, `MaterialConsumptionHistoryItemDto`) and returned field values are unchanged; behavior is observably identical to today for every non-empty page, including the existing `"Neznámý"` fallback for an id that has no resolvable name (e.g., a deleted material referenced by a historical row).

### FR-3: Test double and query-count coverage
Update `MockPackingMaterialRepository` (`backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`) to implement `GetMaterialNamesByIdsAsync` by filtering its in-memory `_materials` list to the supplied ids (mirroring how `GetAllAsync` and `GetRecentLogsForMaterialsAsync` are already faked there), so existing and new handler tests keep working without a real database.

Add a query-count-style test for `GetConsumptionHistoryHandler`, following the existing pattern in `PackingMaterialsListQueryCountTests.cs` (a thin `IPackingMaterialRepository` wrapper around the real `PackingMaterialRepository` with an in-memory `ApplicationDbContext`, counting calls per method), asserting:
- `GetAllAsync` is called **zero** times by `GetConsumptionHistoryHandler.Handle`.
- `GetMaterialNamesByIdsAsync` is called **exactly once**.
- The ids passed to `GetMaterialNamesByIdsAsync` are a subset of (and no larger than) the distinct `PackingMaterialId` values present in the returned page.

**Acceptance criteria:**
- All four existing tests in `GetConsumptionHistoryHandlerTests.cs` pass unchanged in behavior/assertions after the fix (they exercise the same handler outputs, only the internal repository call changes).
- A new test proves `GetAllAsync` is never invoked by this handler and `GetMaterialNamesByIdsAsync` is invoked exactly once with the correct, page-scoped id set.
- `dotnet build` and the full `Anela.Heblo.Tests` PackingMaterials suite pass.

## Non-Functional Requirements

### NFR-1: Performance
- Material-name resolution cost per request becomes `O(distinct material ids on the current page)` instead of `O(total packing materials in the system)`.
- The `GetMaterialNamesByIdsAsync` query touches at most `MaxPageSize` (100) distinct ids per call, regardless of how large the packing-materials table grows.
- No change to the cost or shape of the primary `GetConsumptionHistoryAsync` paginated query.

### NFR-2: Security
- No change to authentication/authorization: `GetMaterialNamesByIdsAsync` is an internal repository method reached only through the existing, already-authorized `GetConsumptionHistoryHandler` request pipeline.
- No new externally-facing surface (no new endpoint, no new request/response DTO field). Data sensitivity is unchanged — material names were already returned in the response.

## Data Model
No schema or entity changes. `PackingMaterial` (`Id`, `Name`, ...) is unchanged. `MaterialConsumptionHistoryRecord` (already a plain class, per this repo's DTO/record convention) is unchanged. The new repository method reads the same `PackingMaterials` table that `GetAllAsync` already reads, just with a narrower filter.

## API / Interface Design
No public HTTP API change — `GetConsumptionHistoryRequest`/`GetConsumptionHistoryResponse` and their generated OpenAPI client shapes are untouched.

Internal interface addition only:

```csharp
// IPackingMaterialRepository.cs
Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
    IEnumerable<int> packingMaterialIds,
    CancellationToken cancellationToken = default);
```

Handler-internal call sequence (unchanged order, second call replaced):
1. `_repository.GetConsumptionHistoryAsync(filter, skip, pageSize, ascending, ct)` → `(records, totalCount)`.
2. `_repository.GetMaterialNamesByIdsAsync(records.Select(r => r.PackingMaterialId).Distinct(), ct)` → `materialNames` dictionary (replaces step 2's prior `GetAllAsync` call).
3. `records.Select(r => MapToDto(r, materialNames))` — unchanged.

## Dependencies
- Entity Framework Core / `ApplicationDbContext` (existing).
- No new external services, packages, or feature flags.
- Depends on the existing `PackingMaterialRepository` base (`BaseRepository<PackingMaterial, int>`) and `IRepository<TEntity, TKey>` conventions already in use in this module.

## Out of Scope
- The alternative "JOIN in `GetConsumptionHistoryAsync`" approach mentioned in the brief (embedding material name directly into the `MaterialConsumptionHistoryRecord` projection to eliminate the second round-trip entirely). That query already unions two heterogeneous sources (`PackingMaterialConsumption` and `PackingMaterialLog`) via `Concat`; adding a join to both branches is a larger, riskier change to an already-complex query and is not required to fix the O(total materials) scaling problem this issue is about. It can be considered separately as a further optimization if the two-round-trip cost ever becomes material.
- Any change to `GetPackingMaterialsListHandler` or other handlers in the PackingMaterials module (they already use the correct targeted-batch pattern).
- Any UI/frontend change — the response contract is unchanged.
- Caching of material names across requests.

## Open Questions
None.

## Status: COMPLETE
