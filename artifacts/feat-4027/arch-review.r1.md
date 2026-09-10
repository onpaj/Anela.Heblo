# Architecture Review: Targeted material-name lookup in GetConsumptionHistoryHandler

## Skip Design: true

## Architectural Fit Assessment

This is a textbook fit, not a stretch. The module already has the exact pattern this fix needs to replicate, one method away: `IPackingMaterialRepository.GetRecentLogsForMaterialsAsync(IEnumerable<int> ids, ...)`, implemented in `PackingMaterialRepository` (`backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs:24-42`) as a `WHERE ids.Contains(...)` query with an empty-collection short-circuit that skips the DB round-trip. `GetPackingMaterialsListHandler` is the consumer precedent: it collects ids from one repository result and passes them into a second, targeted batch call. `GetConsumptionHistoryHandler` currently breaks that convention by discarding the ids it already has (`records.Select(r => r.PackingMaterialId)`) and calling `GetAllAsync` instead — confirmed at `GetConsumptionHistoryHandler.cs:48-49`.

Two structural facts keep this change small and low-risk:
- `IPackingMaterialRepository` already carries feature-specific methods beyond the generic `IRepository<T,TKey>` surface (`GetRecentLogsForMaterialsAsync`, `GetConsumptionHistoryAsync`, etc.), so adding `GetMaterialNamesByIdsAsync` is additive to an interface that already mixes generic and PackingMaterials-specific concerns — no new architectural seam.
- The DI binding for `IPackingMaterialRepository → PackingMaterialRepository` already exists in `PackingMaterialsModule.cs:18`. Per ADR-004, a repository's DI binding lives in its owning feature module, not `PersistenceModule`. This fix adds a method to an already-correctly-wired interface/implementation pair — **no new DI registration is required or permitted**.

No API contract, DTO shape, or module boundary changes. `MaterialConsumptionHistoryRecord` and the response DTOs are untouched, and per this repo's DTO convention (classes, never records — `docs/architecture/development_guidelines.md` / project CLAUDE.md) that record is already a plain `class` (`Domain/Features/PackingMaterials/MaterialConsumptionHistoryRecord.cs`), so no conversion work is needed there either.

## Proposed Architecture

### Component Overview

```
GetConsumptionHistoryHandler.Handle
   │
   ├─ 1. _repository.GetConsumptionHistoryAsync(filter, skip, take, asc, ct)
   │        → (records: IReadOnlyList<MaterialConsumptionHistoryRecord>, totalCount)
   │        [UNION of PackingMaterialConsumption + PackingMaterialLog, already paginated]
   │
   ├─ 2. materialIds = records.Select(r => r.PackingMaterialId).Distinct()
   │
   ├─ 3. _repository.GetMaterialNamesByIdsAsync(materialIds, ct)   ◄── NEW, replaces GetAllAsync
   │        → IReadOnlyDictionary<int, string>
   │        [WHERE Id IN (...) on PackingMaterials, empty-ids ⇒ no query, mirrors
   │         GetRecentLogsForMaterialsAsync's shape exactly]
   │
   └─ 4. records.Select(r => MapToDto(r, materialNames)) — unchanged
```

This is a same-shape sibling to the existing `GetRecentLogsForMaterialsAsync` batch method — same repository, same "collect ids from a prior result, pass them into a targeted Contains() query" idiom, same empty-input contract. No new component, no new module, no new cross-cutting concern.

### Key Design Decisions

#### Decision 1: Targeted `WHERE Id IN (...)` lookup vs. JOIN into `GetConsumptionHistoryAsync`

**Options considered:**
1. Add `GetMaterialNamesByIdsAsync(ids, ct) → IReadOnlyDictionary<int,string>` and call it after pagination (this spec's approach).
2. Embed the material name directly into the `MaterialConsumptionHistoryRecord` projection via a JOIN in `GetConsumptionHistoryAsync`, eliminating the second round-trip entirely.

**Chosen approach:** Option 1, as specified.

**Rationale:** `GetConsumptionHistoryAsync` already `Concat`s two heterogeneous sources (`PackingMaterialConsumption` and `PackingMaterialLog`) into one `IQueryable<MaterialConsumptionHistoryRecord>`, and the `Concat` happens *before* `Skip/Take`, i.e. before the query is paginated. Joining `PackingMaterials` into *both* branches of that concat, in a way that still lets EF Core translate `Skip/Take` over the unioned+joined shape, is a materially larger and riskier change to an already-complex query — and it's not required to fix the O(total materials) scaling problem, which is entirely about the second call, not the first. Option 1 is a two-line handler change plus one new, narrow repository method that mirrors an existing, already-tested pattern (`GetRecentLogsForMaterialsAsync`) almost verbatim. This matches the spec's "Out of Scope" call and I concur with it: option 2 is a legitimate follow-up if the two-round-trip cost ever becomes material, not a blocker here.

#### Decision 2: Projected anonymous/dictionary result, not full `PackingMaterial` entities

**Options considered:**
1. `DbSet.Where(m => ids.Contains(m.Id)).ToDictionary(m => m.Id, m => m.Name)` (materializes full entities, then projects in memory).
2. `DbSet.Where(m => ids.Contains(m.Id)).Select(m => new { m.Id, m.Name }).ToDictionaryAsync(...)` (server-side projection to `Id`/`Name` only).

**Chosen approach:** Option 2, as specified in FR-1.

**Rationale:** The handler only ever needs `Id → Name`. Projecting server-side keeps the query minimal (two scalar columns instead of the full `PackingMaterial` row set, including any future wide/JSON columns) and avoids attaching tracked entities to the context for a read-only lookup — consistent with `GetRecentLogsForMaterialsAsync`, which also projects/groups results rather than round-tripping full aggregates further than necessary. Note EF Core's LINQ provider does not support `.ToDictionaryAsync` directly on an anonymous projection with an async-enumeration step in all versions consistently — the safest, most literal implementation is `.Select(...).ToListAsync(ct)` then `.ToDictionary(x => x.Id, x => x.Name)` in memory over the (small, page-bounded) result. Implementers should use that two-step form rather than fighting `ToDictionaryAsync` translation.

#### Decision 3: Reuse the `GetRecentLogsForMaterialsAsync` empty-collection contract verbatim

**Options considered:**
1. Let an empty `ids` collection fall through to `WHERE Id IN ()`, which EF Core/Npgsql will still execute as a (trivially-false, but real) round trip.
2. Explicitly short-circuit: `if (ids.Count == 0) return empty dictionary;` before touching the `DbSet`.

**Chosen approach:** Option 2, matching `GetRecentLogsForMaterialsAsync`'s existing short-circuit exactly (same `ids as IReadOnlyCollection<int> ?? ids.ToArray()` materialization idiom, same early return).

**Rationale:** `GetConsumptionHistoryAsync` returning `records.Count == 0` (e.g., an out-of-range page or an empty filter result) is a real, reachable case, and FR-2's acceptance criteria requires no full-table fallback and no round trip in that path. Reusing the sibling method's exact idiom (materialize to a collection, check `.Count`, short-circuit) rather than inventing a new one keeps the two batch methods on `IPackingMaterialRepository` visibly consistent for future readers.

## Implementation Guidance

### Directory / Module Structure

No new files, no new directories, no new DI registration. Every change lands in an existing file:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` | Add `Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(IEnumerable<int> packingMaterialIds, CancellationToken cancellationToken = default);` — place it directly after `GetRecentLogsForMaterialsAsync` (same "bulk lookup keyed by material id" family), with an XML doc comment on the empty-input contract mirroring that method's existing doc comment style. |
| `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` | Implement the method, placed near `GetRecentLogsForMaterialsAsync` for locality. |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs` | Replace lines 48-49 (`GetAllAsync` + `ToDictionary`) with the distinct-id extraction + `GetMaterialNamesByIdsAsync` call. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` | Implement `GetMaterialNamesByIdsAsync` against the in-memory `_materials` list. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs` **or** a new sibling `GetConsumptionHistoryQueryCountTests.cs` | Add the query-count test (see Decision below on file placement). |

**File-placement note for the new test:** the spec says "add a query-count-style test... following the existing pattern in `PackingMaterialsListQueryCountTests.cs`" without being explicit about whether it's a new `[Fact]` in that same file or a new file. **Guidance: create a new file**, `GetConsumptionHistoryQueryCountTests.cs`, in the same directory, rather than adding an unrelated handler's test into `PackingMaterialsListQueryCountTests`. That class is named after, scoped to, and documented (via its file-level comment) as covering `GetPackingMaterialsListHandler` specifically; stuffing a `GetConsumptionHistoryHandler` test into it would misname the file relative to its contents. Copy the `CountingRepositoryWrapper` pattern (a full `IPackingMaterialRepository` pass-through wrapper around a real `PackingMaterialRepository` backed by an EF Core in-memory `ApplicationDbContext`, counting only the methods under test) into the new file — do not try to share one wrapper class across both test files, since each wrapper's counted-method set is intentionally handler-specific and the existing one is `private sealed` and file-local by design.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs
// Insert directly after GetRecentLogsForMaterialsAsync's declaration.

/// <summary>
/// Resolves display names for a set of packing materials by id. Ids with no matching
/// material are simply absent from the result (no exception). When <paramref name="packingMaterialIds"/>
/// is empty, returns an empty dictionary without executing a database query.
/// </summary>
/// <param name="packingMaterialIds">The packing material identifiers to resolve names for.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Dictionary of <c>Id -> Name</c> for the ids that exist.</returns>
Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
    IEnumerable<int> packingMaterialIds,
    CancellationToken cancellationToken = default);
```

```csharp
// Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs
// Insert directly after GetRecentLogsForMaterialsAsync's implementation.

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

Use `DbSet` (inherited `protected` field from `BaseRepository<PackingMaterial, int>`), matching `GetAllWithAllocationsAsync`/`GetByIdWithAllocationsAsync`'s convention of using `DbSet` for plain `PackingMaterials`-table queries, as opposed to `Context.Set<T>()` which this file uses only for the *other* entity types (`PackingMaterialLog`, `PackingMaterialConsumption`, `PackingMaterialDailyRun`) that don't have their own `DbSet` field on this repository.

```csharp
// GetConsumptionHistoryHandler.cs — replace lines 48-49

var materialIds = records.Select(r => r.PackingMaterialId).Distinct();
var materialNames = await _repository.GetMaterialNamesByIdsAsync(materialIds, cancellationToken);
```

No change to `MapToDto`, `GetConsumptionHistoryResponse`, `MaterialConsumptionHistoryItemDto`, or any controller/OpenAPI-facing type.

### Data Flow

Unchanged end-to-end except for the source of `materialNames`:

1. Request arrives at the existing controller action → `GetConsumptionHistoryRequest` → `ValidationBehavior` → `GetConsumptionHistoryHandler.Handle`.
2. `GetConsumptionHistoryAsync` executes the paginated `Concat`+`OrderBy`+`Skip`+`Take` query exactly as today — **untouched**.
3. **Changed step:** distinct `PackingMaterialId`s are pulled from the *already-materialized, already-paginated* `records` list (in-memory `Distinct()`, no query), then passed to `GetMaterialNamesByIdsAsync`, which issues one `WHERE Id IN (...)` query scoped to at most `MaxPageSize` (100) ids.
4. `MapToDto` looks up each record's name in the returned dictionary exactly as today, falling back to `"Neznámý"` on a miss (deleted material, or historical row referencing an id that no longer resolves) — this fallback path is unchanged and must continue to be exercised by the existing `Handle_UnknownMaterial_FallsBackToPlaceholderName` test.
5. Response assembly (`TotalCount`, `PageNumber`, `PageSize`, `TotalPages`) — untouched.

Net effect: total DB round-trips per request stays at exactly two (one for `GetConsumptionHistoryAsync`, one for name resolution), same as today — only the second one's cost changes from O(all materials) to O(≤100 distinct ids), and the ordering guarantee ("names resolved from the current page's contents, not from an unpaginated source") the spec requires is enforced structurally, since `records` is the *output* of the first, already-paginated call.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `IPackingMaterialRepository` is implemented by both `PackingMaterialRepository` (production) and `MockPackingMaterialRepository` (test double) plus the file-local `CountingRepositoryWrapper` in the list-handler query-count test. Adding a method to the interface without updating all implementers breaks the build. | Low | Interface + all three implementers are explicitly enumerated in FR-1/FR-3 and above; `dotnet build` will fail loudly (missing interface member) if any is missed, so this is self-catching, not silent. |
| EF Core's `.Select(...).ToDictionaryAsync(...)` on an anonymous type sometimes fails to translate or behaves surprisingly depending on EF Core version; a naive literal implementation could break at runtime despite compiling. | Low | Guidance above specifies the safe two-step form (`.Select(...).ToListAsync()` then in-memory `.ToDictionary(...)`) rather than a direct `ToDictionaryAsync` on the projection. |
| Test-double drift: `MockPackingMaterialRepository.GetAllAsyncWasCalled` flag exists specifically to let *other* tests assert `GetAllAsync` was/wasn't called; if a future refactor of `GetConsumptionHistoryHandler` reintroduces a `GetAllAsync` call, only the new query-count test (real EF Core-backed) would catch it — the mock-based `Handle_*` tests in `GetConsumptionHistoryHandlerTests.cs` don't assert on `GetAllAsyncWasCalled` today and this spec doesn't ask them to. | Low | Acceptable as scoped — FR-3's new query-count test is the regression guard for this specific concern, exactly mirroring how `PackingMaterialsListQueryCountTests` guards `GetPackingMaterialsListHandler`. No action needed beyond what FR-3 already specifies. |
| Placing the new query-count test inside the existing `PackingMaterialsListQueryCountTests.cs` file (rather than a new file) would misname/overload that file and could tempt sharing the `private sealed CountingRepositoryWrapper` across two unrelated handlers, coupling their counted-method sets. | Low | Explicit guidance above: new file `GetConsumptionHistoryQueryCountTests.cs`, new file-local wrapper class, following the same structural pattern but not the same class. |

## Specification Amendments

1. **FR-1 implementation detail:** the spec's example projection (`DbSet.Where(m => ids.Contains(m.Id)).Select(m => new { m.Id, m.Name })`) should be completed with `.ToListAsync(cancellationToken)` followed by an in-memory `.ToDictionary(...)`, not `.ToDictionaryAsync(...)` directly on the anonymous projection — added to Implementation Guidance above to avoid an EF Core translation pitfall during implementation.
2. **FR-3 test-file placement (clarification, not a functional change):** the new query-count test belongs in its own file, `GetConsumptionHistoryQueryCountTests.cs`, alongside `PackingMaterialsListQueryCountTests.cs`, with its own file-local `CountingRepositoryWrapper` — not appended into the existing list-handler-named file. This is a naming/organization clarification only; the spec's assertions (zero `GetAllAsync` calls, exactly one `GetMaterialNamesByIdsAsync` call, page-scoped id set) are unchanged.
3. No functional, contract, or scope amendments — the spec's FR-1/FR-2/FR-3 acceptance criteria are implementable as written and align with existing repository, handler, and test-double conventions confirmed by direct inspection of `PackingMaterialRepository.cs`, `GetPackingMaterialsListHandler.cs`, `MockPackingMaterialRepository.cs`, and `PackingMaterialsListQueryCountTests.cs`.

## Prerequisites

None. No migration, no config, no feature flag, no new infrastructure. This is a same-module, additive-interface-method change against code and conventions that already exist and are already exercised by tests; implementation can start immediately.
