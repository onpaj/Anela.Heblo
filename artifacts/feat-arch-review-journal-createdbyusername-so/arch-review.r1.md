# Architecture Review: Fix Journal List Sort by Author (CreatedByUsername)

## Skip Design: true

This is a pure backend correctness fix — one switch arm added, one dead arm removed, a structured warning log, and supporting tests. The existing `SortableHeader` already sends the correct value; no UI components are added, changed, or restyled.

## Architectural Fit Assessment

The fix lives entirely inside the existing **Vertical Slice** for `Journal` (`backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`). It does not cross any module boundary:

- **Domain** (`Anela.Heblo.Domain.Features.Journal.JournalEntry`) — already exposes `CreatedByUsername` (string?, MaxLength 100). No domain change.
- **Application** (`GetJournalEntries`, `SearchJournalEntries` handlers and their DTOs `GetJournalEntriesRequest.SortBy/SortDirection` and `SearchJournalEntriesRequest.SortBy/SortDirection`) — the HTTP contract surface (`string SortBy`, `string SortDirection`) is unchanged; only the set of accepted values for `SortBy` widens by one.
- **Persistence** — only `JournalRepository` changes. EF Core LINQ translation handles the new `OrderBy(x => x.CreatedByUsername)` natively.
- **Frontend** — already sends `column="createdByUsername"` (`JournalList.tsx:411`) and `sortDirection: sortDescending ? "DESC" : "ASC"` (`JournalList.tsx:186, 194`). No change.

The fix matches the project's general repository pattern (LINQ + EF Core, paginated `PagedResult<T>`, logging via injected `ILogger<JournalRepository>` — already wired in the constructor at line 11–18). The integration test class `JournalRepositoryIntegrationTests` already uses `Microsoft.EntityFrameworkCore.InMemory` with a mocked `ILogger<JournalRepository>`, so new sort tests slot in without additional infrastructure.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────┐
│  Frontend (unchanged)                                              │
│   JournalList.tsx                                                  │
│     SortableHeader column="createdByUsername" (line 411)           │
│     payload: { sortBy: "createdByUsername", sortDirection: "ASC" } │
└──────────────────────────────┬─────────────────────────────────────┘
                               │ HTTP
┌──────────────────────────────▼─────────────────────────────────────┐
│  Application (unchanged contracts)                                 │
│   GetJournalEntriesRequest    { SortBy, SortDirection }            │
│   SearchJournalEntriesRequest { SortBy, SortDirection }            │
│   → GetJournalEntriesHandler / SearchJournalEntriesHandler         │
└──────────────────────────────┬─────────────────────────────────────┘
                               │
┌──────────────────────────────▼─────────────────────────────────────┐
│  Persistence (CHANGED)                                             │
│   JournalRepository                                                │
│     GetEntriesAsync ────────┐                                      │
│                             ├──► ApplySort(query, sortBy,          │
│     SearchEntriesAsync ─────┘              sortDirection,          │
│                                            _logger)                │
│                                                                    │
│     ApplySort (NEW private static helper)                          │
│       "title"             → OrderBy(Title)                         │
│       "createdbyusername" → OrderBy(CreatedByUsername)             │
│                                .ThenByDescending(EntryDate)        │
│       null/empty          → OrderBy(EntryDate)    [default]        │
│       unknown non-empty   → OrderBy(EntryDate) + log Warning       │
└────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Keep `string sortDirection`, do not convert to `bool sortDescending`
**Options considered:**
- (A) Adopt the spec's proposed helper signature `bool sortDescending`.
- (B) Keep the existing `string sortDirection` ("ASC" / anything-else) throughout the helper.

**Chosen approach:** (B). The helper signature is:
```csharp
private static IQueryable<JournalEntry> ApplySort(
    IQueryable<JournalEntry> query,
    string? sortBy,
    string sortDirection,
    ILogger logger);
```

**Rationale:** The spec proposes `bool sortDescending`, but the entire contract pipeline — `GetJournalEntriesRequest.SortDirection` (line 13), `SearchJournalEntriesRequest.SortDirection` (line 26), the `IJournalRepository` interface (lines 11, 24), and the two handlers — uses `string SortDirection`. Converting only inside the helper introduces a needless conversion at the call site and a mismatch between the spec's wording and what's actually shippable as a "surgical" fix. The cost of mismatching the rest of the module is higher than the cost of mismatching the spec.

#### Decision 2: Consolidate the duplicated sort switch into one private helper
**Options considered:**
- (A) Inline-fix both sites (lines 44–55 and 135–146) and accept the duplication.
- (B) Extract a `private static ApplySort` helper used by both `GetEntriesAsync` and `SearchEntriesAsync`.

**Chosen approach:** (B). Matches the spec.

**Rationale:** The duplication is precisely how this bug shipped — two switches drifted independently. Consolidation is the structural fix that prevents recurrence. `private static` (no instance state needed; logger passed in) keeps the helper testable in isolation.

#### Decision 3: Stable tiebreaker for author sort is `ThenByDescending(EntryDate)`
**Options considered:**
- (A) Tiebreak by `EntryDate DESC` (matches default sort direction, newest-first).
- (B) Tiebreak by `Id` (guaranteed unique, but reveals insertion order to the UI).
- (C) No tiebreaker.

**Chosen approach:** (A) — matches FR-1 acceptance criteria.

**Rationale:** `EntryDate DESC` is the existing default ordering, so within an author bucket users see what they already expect ("most recent first"). `Id` is unique but semantically meaningless to a UI viewer. No tiebreaker causes nondeterministic page boundaries — repeated paginated requests can return overlapping or missing rows.

#### Decision 4: Unknown-key warning is emitted only for non-empty, unrecognized values
**Options considered:**
- (A) Log on every default-branch hit (including null/empty).
- (B) Log only when the caller explicitly sent a non-empty value that didn't match.
- (C) Reject with HTTP 400.

**Chosen approach:** (B) — matches FR-3.

**Rationale:** (A) would spam the log on every legitimate empty-default request — destroying the signal we're trying to add. (C) is a breaking contract change and explicitly out of scope. (B) gives us the one piece of telemetry we actually need: "the frontend sent a `sortBy` no backend code understands."

The structured property must be `{SortBy}` so Application Insights queries can filter on it (NFR-4). Use `LoggerMessage` source-generated logging or `_logger.LogWarning("Unknown sort key {SortBy} requested on {Repository}", sortBy, nameof(JournalRepository))` — never string-interpolate the value into the message template.

#### Decision 5: Defer the `CreatedByUsername` index decision; measure first
**Options considered:**
- (A) Add the EF Core migration with a non-unique index up front.
- (B) Ship the sort fix without an index; measure; add the index in a follow-up migration only if NFR-1 fails.

**Chosen approach:** (B) — matches NFR-1's "if needed" wording.

**Rationale:** The current dataset is a single-tenant cosmetics workspace; Journal volume is small. Premature indexing adds write-path cost for a query that may never run hot. Add the index when measurement shows the 1.2x threshold has been crossed, not before.

## Implementation Guidance

### Directory / Module Structure

All changes occur in two files plus one new test:

```
backend/src/Anela.Heblo.Persistence/Catalog/Journal/
  JournalRepository.cs                                  [MODIFIED]
    - Remove "createdat" case from both switches
    - Add "createdbyusername" case
    - Extract switches into private static ApplySort
    - Emit warning log on non-empty unknown sortBy

backend/test/Anela.Heblo.Tests/Features/Journal/
  JournalRepositoryIntegrationTests.cs                  [MODIFIED]
    - Add tests:
      * GetEntriesAsync_SortsByCreatedByUsername_Ascending
      * GetEntriesAsync_SortsByCreatedByUsername_Descending
      * GetEntriesAsync_SortsByCreatedByUsername_CaseInsensitiveKey
      * GetEntriesAsync_SortByCreatedByUsername_TiebreaksByEntryDateDesc
      * GetEntriesAsync_UnknownSortBy_LogsWarningAndDefaults
      * GetEntriesAsync_NullOrEmptySortBy_DoesNotLogWarning
      * SearchEntriesAsync_SortsByCreatedByUsername_Ascending
        (single mirror to confirm both call-sites share the helper)
```

No new files, no new projects, no new migrations (unless NFR-1 measurement forces an index — in which case one migration under `backend/src/Anela.Heblo.Persistence/Migrations/`).

### Interfaces and Contracts

**`IJournalRepository` (unchanged.)** The interface signature for `GetEntriesAsync` and `SearchEntriesAsync` remains identical. Only the implementation of `sortBy` parsing changes.

**`ApplySort` (new, private, non-API).**
```csharp
private static IQueryable<JournalEntry> ApplySort(
    IQueryable<JournalEntry> query,
    string? sortBy,
    string sortDirection,
    ILogger logger)
{
    var ascending = string.Equals(sortDirection, "ASC", StringComparison.OrdinalIgnoreCase);

    return sortBy?.ToLowerInvariant() switch
    {
        "title" => ascending
            ? query.OrderBy(x => x.Title)
            : query.OrderByDescending(x => x.Title),

        "createdbyusername" => ascending
            ? query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)
            : query.OrderByDescending(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate),

        null or "" => ApplyDefaultSort(query, ascending),

        _ => ApplyDefaultSortWithWarning(query, ascending, sortBy, logger),
    };
}
```

Two helper expressions (`ApplyDefaultSort` and `ApplyDefaultSortWithWarning`) keep the switch arms expression-bodied and prevent accidental re-introduction of side effects in the null/empty path. Use `ToLowerInvariant()` (not `ToLower()`) — the latter is culture-dependent and an analyzer flag (CA1304) that the project's `dotnet format` will likely raise.

Note: `sortBy` is whitespace-tolerated by FR-3. Treat `string.IsNullOrWhiteSpace(sortBy)` as the "no warning, default sort" branch — the spec's "null, empty, or whitespace" acceptance criterion is not satisfied by the `null or ""` pattern alone.

### Data Flow

Single use case — paginated list of journal entries sorted by author, ascending:

1. User clicks "Autor" header in `JournalList.tsx`.
2. `handleSort('createdByUsername')` toggles `sortDescending` (lines 238–239), triggers query refetch.
3. Frontend sends `GET /api/.../journal?sortBy=createdByUsername&sortDirection=ASC&pageNumber=1&pageSize=...`.
4. MVC controller binds to `GetJournalEntriesRequest` (or `SearchJournalEntriesRequest` when filters are active).
5. MediatR dispatches to the corresponding handler.
6. Handler calls `_journalRepository.GetEntriesAsync(..., sortBy: "createdByUsername", sortDirection: "ASC", ...)`.
7. Repository builds the base query, calls `ApplySort(query, "createdByUsername", "ASC", _logger)`.
8. `ApplySort` lowercases to `"createdbyusername"`, matches the new case, returns `query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)`.
9. `CountAsync` runs (`SELECT COUNT(*)`), then `Skip`/`Take`/`ToListAsync` runs (`SELECT ... ORDER BY CreatedByUsername ASC, EntryDate DESC OFFSET ... FETCH ...`).
10. `PagedResult<JournalEntry>` returns up the stack, gets mapped to DTOs, returned to the frontend.

Unknown-key path (e.g. a future frontend regression sends `sortBy=Tags`):

1–6. Same as above with `sortBy="Tags"`.
7. `ApplySort` lowercases to `"tags"`, falls to the `_` arm.
8. Helper logs `LogWarning("Unknown sort key {SortBy} requested on {Repository}", "Tags", "JournalRepository")`, returns default ordering.
9–10. Query executes normally; user sees default-sorted results; ops sees one warning event in Application Insights with structured `SortBy=Tags` property.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Case-insensitive ordering tests pass on the EF Core InMemory provider but break under Postgres collation (or vice versa) | Medium | Don't assert on case-folded ordering in unit tests — assert on the SQL-side ordering of deterministic input (all-same-case usernames). The InMemory provider does ordinal C# string comparison; production DB uses the column collation. Acceptable divergence as long as the spec's "case-insensitive string comparison, using the database collation" wording stays — the test verifies *that we ordered*, not the collation rules. |
| The unknown-key warning becomes noisy if any non-Journal caller (e.g. a third-party integration test, a stale Postman collection) hits this endpoint with junk values | Low | The log is `Warning` not `Error`, structured by `{SortBy}`, and emits at most once per request. App Insights filtering on `SortBy` neutralizes the noise. |
| Extracting the helper changes the surrounding method shape, tempting unrelated refactors (filter consolidation, etc.) | Low | The fix is surgical per project rule. Touch only the sort block. Leave the filter, paging, include-tree, and `CountAsync` lines exactly as they are. |
| The `ThenByDescending(EntryDate)` tiebreaker doubles the cost of every author-sort query | Low | `EntryDate` is the existing default sort column and is read on every list query already. EF Core will translate to a single `ORDER BY ... , ...` — no extra round-trip. If NFR-1 measurement fails, the index added under "Prerequisites" should cover `(CreatedByUsername, EntryDate DESC)`. |
| Logging `sortBy` verbatim creates a (theoretical) log-injection vector via control characters | Low | The value flows through Application Insights as a structured property, not a console line. Don't string-interpolate into the message template (already covered in Decision 4). |

## Specification Amendments

1. **Replace `bool sortDescending` with `string sortDirection` in the proposed `ApplySort` signature** (spec § "API / Interface Design"). The rest of the Journal contract uses `string SortDirection` ("ASC" / "DESC"); converting at the helper boundary is unnecessary and creates a coupling the spec doesn't justify. See Decision 1 above for the corrected signature.

2. **Use `ToLowerInvariant()` rather than `ToLower()`** when matching the `sortBy` key. The spec inherits `.ToLower()` from the existing code, but `dotnet format` / Roslyn analyzers will flag CA1304 (specify culture). Fixing this is in scope as part of the consolidation since the line is being rewritten anyway.

3. **`string.IsNullOrWhiteSpace(sortBy)`, not just `null or ""`**, defines the "no warning, default sort" branch (spec § FR-3 acceptance criterion 1 already mentions whitespace; the proposed switch expression in the spec doesn't capture it).

4. **`SearchEntriesAsync` test mirror is required, not optional.** The spec lists tests for `GetEntriesAsync` only. Without at least one test against `SearchEntriesAsync` that exercises the new sort key, future code could re-duplicate the switch without test signal. One mirror test is enough since both methods will share `ApplySort`.

5. **Clarify NFR-1 measurement procedure.** "Within 1.2x of sort-by-title" requires a measurement scaffolding the spec doesn't define. Recommend deferring the threshold check to manual observation in staging after deployment, and treating index addition as a separate follow-up if and only if a slow-query alert fires. The spec language already permits this ("If a covering index... is needed"), but should be explicit that the implementation PR does not need to measure before merging.

## Prerequisites

None. The fix requires:

- No database migration (unless NFR-1 measurement triggers the optional index — out of scope for the primary PR per Amendment 5).
- No new NuGet packages.
- No configuration changes.
- No infrastructure or Key Vault updates.
- No frontend changes.
- No new endpoints, auth surface, or feature flags.

`ILogger<JournalRepository>` is already injected (`JournalRepository.cs:11–18`); the warning log requires only a new call to the existing logger. The `Microsoft.Extensions.Logging` namespace is already imported at the top of the file.