# Specification: Fix Journal List Sort by Author (CreatedByUsername)

## Summary
The Journal list page sends `createdByUsername` as the `sortBy` parameter when the user clicks the "Autor" column header, but the backend repository does not handle this value and silently falls back to sorting by `EntryDate`. This spec defines the fix: extend the backend sort switch to handle `createdbyusername`, remove the unreachable `createdat` branch, and add tests to prevent regressions and detect future contract drift.

## Background
The Journal module exposes a paginated list with sortable column headers. Sorting is implemented as a client-driven contract: the frontend sends a `sortBy` string and a `sortDescending` flag; the backend repository maps these to LINQ `OrderBy` / `OrderByDescending` calls.

Currently the contract is broken in two places:

- **Mismatch:** Frontend (`frontend/src/components/pages/Journal/JournalList.tsx:411`) sends `column="createdByUsername"` for the "Autor" header. Backend (`backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:44–55, 135–146`) only matches `"title"` and `"createdat"` after `.ToLower()`. The string `"createdbyusername"` matches neither and silently falls through to the default `EntryDate` sort.
- **Dead code:** No frontend column sends `createdat`, so the backend's `"createdat"` branch is unreachable.

The result is a UX defect: clicking "Autor" appears to re-sort the table (rows shuffle because `EntryDate` is also the default), but the column actually has no effect. There is no error, no warning, and no log entry, making this class of contract bug invisible to ops and to tests.

The suggested fix in the arch-review brief offers two paths. **This spec selects Option 1 (add the backend handler)** because:
1. The "Autor" column is visible and discoverable in the UI; users have a reasonable expectation that clicking it sorts by author.
2. Implementation cost is trivial (one switch case + tests).
3. Removing the header (Option 2) would silently shrink functionality without addressing the underlying contract-drift problem.

## Functional Requirements

### FR-1: Sort Journal list by author username (ascending and descending)
The backend Journal repository must handle `sortBy = "createdByUsername"` (case-insensitive) and order results by `JournalEntry.CreatedByUsername`. The `sortDescending` flag must invert the order in the standard way.

**Acceptance criteria:**
- Calling the Journal list endpoint with `sortBy=createdByUsername&sortDescending=false` returns entries ordered by `CreatedByUsername` ascending (case-insensitive string comparison, using the database collation).
- Calling with `sortBy=createdByUsername&sortDescending=true` returns the same ordering reversed.
- The match is case-insensitive — `CreatedByUsername`, `createdByUsername`, and `CREATEDBYUSERNAME` all map to the same sort.
- When two entries share an identical `CreatedByUsername`, a stable secondary sort by `EntryDate DESC` is applied so paginated results remain deterministic across requests.
- Clicking the "Autor" header in the Journal list UI re-orders the visible rows by author name and toggles ascending/descending on repeat clicks. No frontend change is required — the existing `SortableHeader` already emits the correct value.

### FR-2: Remove dead `createdat` sort branch
The `"createdat"` case in the repository sort switch is unreachable from the current frontend and must be removed as part of this change.

**Acceptance criteria:**
- The `"createdat"` literal no longer appears in `JournalRepository.cs`.
- No frontend code references `createdat` as a sort column (confirmed via grep over `frontend/src/`).
- Removal does not change behavior for any currently-used sort value (`title`, `createdByUsername`, default).

### FR-3: Surface unknown sort keys instead of silently defaulting
To prevent the next instance of this contract drift from going undetected, an unknown `sortBy` value must be observable. The backend should log a warning when `sortBy` is non-null/non-empty and does not match any known column, then continue with the default sort.

**Acceptance criteria:**
- When `sortBy` is `null`, `""`, or whitespace, no warning is logged and the default sort applies (current behavior preserved).
- When `sortBy` is a non-empty value that does not match a known column (after `ToLower()`), a single structured warning is logged at `LogLevel.Warning` with the unrecognized value and the repository name. The response still succeeds with the default sort applied (no HTTP error, no behavior change for end users).
- The warning message does not include user-provided content beyond the `sortBy` string itself (avoid log injection from arbitrary query params).

## Non-Functional Requirements

### NFR-1: Performance
- Sorting by `CreatedByUsername` must use a database `ORDER BY` (not in-memory sort over the full table). The existing pagination (`Skip`/`Take`) and total-count query must continue to execute at the database.
- Query cost for sort-by-author must be within 1.2x of the existing sort-by-title query for the same dataset size. If a covering index on `CreatedByUsername` is needed to meet this, it is in scope and must be added via a new EF Core migration.

### NFR-2: Security
- No new endpoints, no new data exposure, no new auth surface. The change is internal to an existing authorized endpoint.
- The warning log added in FR-3 must not log secrets, tokens, or PII beyond what is already logged for this request. `sortBy` is a UI-supplied column key; logging it verbatim is acceptable.

### NFR-3: Backward compatibility
- Existing requests with `sortBy=title`, `sortBy=Title`, empty/null, or any other historically-defaulting value must produce the same ordering they produced before this change.
- The removal of the `createdat` branch must not cause a behavior change for any current caller — verified by FR-2 acceptance criteria (no frontend reference) and by the fact that the previous `createdat` branch sorted by the same column as the default fallback semantically (both deterministic by date), so any unknown caller falling back to default sees no functional regression.

### NFR-4: Observability
- The warning log from FR-3 must include a structured property (e.g. `{SortBy}`) so it can be filtered in Application Insights. This enables alerting on future contract drift without log-message-string matching.

## Data Model
No schema changes. The fix uses the existing `JournalEntry.CreatedByUsername` column (already populated on entry creation).

If NFR-1 query-cost target is not met without an index, add an EF Core migration that creates a non-unique index on `JournalEntries.CreatedByUsername`. This is the only data-layer change in scope, and only if measured to be necessary.

## API / Interface Design

### Repository sort switch (after fix)
The `JournalRepository` sort logic — currently appearing twice (lines 44–55 and 135–146 of `JournalRepository.cs`) — should be consolidated into a single private helper to eliminate the duplication that allowed the two branches to drift independently. The helper signature:

```csharp
private static IQueryable<JournalEntry> ApplySort(
    IQueryable<JournalEntry> query,
    string? sortBy,
    bool sortDescending,
    ILogger logger);
```

Switch contents after fix:
- `"title"` → `OrderBy(x => x.Title)` (existing)
- `"createdbyusername"` → `OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)` (new — see FR-1 tiebreaker)
- default → `OrderByDescending(x => x.EntryDate)` (existing) + warning log if `sortBy` was non-empty (FR-3)

### HTTP contract
No change to the request/response schema. The same `GET` list endpoint accepts the same `sortBy` and `sortDescending` query parameters and returns the same DTO shape. Only the set of accepted `sortBy` values expands by one.

### Frontend
No frontend code change required. The existing `SortableHeader column="createdByUsername"` in `JournalList.tsx:411` already sends the correct value; it just had nothing answering it on the backend.

## Dependencies
- `Anela.Heblo.Persistence.Catalog.Journal.JournalRepository` (modified)
- `Anela.Heblo.Domain.Catalog.Journal.JournalEntry` (read-only — already has `CreatedByUsername`)
- EF Core migration tooling (only if NFR-1 forces the index — confirm first by measuring)
- `Microsoft.Extensions.Logging.ILogger<JournalRepository>` (likely already injected; verify during implementation)

No new NuGet packages, no new external services.

## Out of Scope
- Adding sortable columns beyond `Title` and `CreatedByUsername` (e.g. tags, category, status). If the product wants more sortable columns later, that is a separate feature.
- Refactoring `SortableHeader` or any other frontend sort UI component.
- Server-side validation that rejects unknown `sortBy` values with HTTP 400. FR-3 deliberately chooses logging over rejection to preserve backward compatibility; promoting to a hard error is a separate, breaking decision.
- A repo-wide audit of other modules for the same frontend/backend sort-key drift pattern. Worth doing eventually but not part of this fix.
- Localization or display-name handling for the sort column. `CreatedByUsername` is the stored username string; sorting is by raw value.

## Open Questions
None.

## Status: COMPLETE