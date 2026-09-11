# Specification: Deduplicate pagination metadata calculation in Journal handlers

## Summary
`GetJournalEntriesHandler` and `SearchJournalEntriesHandler` each independently compute the same three pagination metadata fields (`TotalPages`, `HasNextPage`, `HasPreviousPage`) using identical formulas. Extract this calculation into a single shared helper local to the Journal feature folder so the formula exists in exactly one place. This is a pure internal refactor: no request/response contracts, endpoints, or behavior change for callers.

## Background
An architecture-review finding (issue #4081) identified that both handlers repeat:

```csharp
TotalPages = (int)Math.Ceiling((double)result.TotalCount / request.PageSize),
HasNextPage = request.PageNumber * request.PageSize < result.TotalCount,
HasPreviousPage = request.PageNumber > 1
```

Both response DTOs (`GetJournalEntriesResponse`, `SearchJournalEntriesResponse`) expose an identical pagination shape (`TotalCount`, `PageNumber`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`). Because the two handlers are siblings in the same module, a future correction to the formula (e.g. an off-by-one at an exact page boundary) could be applied to only one handler, producing a silent, hard-to-detect divergence between `GET /journal-entries` and `GET /journal-entries/search` behavior.

A similar literal pattern also exists in `Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs`, but per the issue's suggested fix this refactor is scoped to the Journal module only — no cross-module abstraction is introduced.

## Functional Requirements

### FR-1: Single shared pagination-metadata calculation for Journal handlers
Introduce one calculation (a static method) that both `GetJournalEntriesHandler.Handle` and `SearchJournalEntriesHandler.Handle` call to obtain `TotalPages`, `HasNextPage`, and `HasPreviousPage`, replacing the inline duplicated expressions in both handlers.

**Acceptance criteria:**
- Both handlers no longer contain their own copies of the `TotalPages` / `HasNextPage` / `HasPreviousPage` expressions; both call the same shared calculation instead.
- For a given `(totalCount, pageNumber, pageSize)` triple, the values produced are byte-for-byte identical to what the current inline formulas produce today (this is a refactor, not a behavior change) — verified by existing/updated unit tests for both handlers.
- The calculation lives inside the Journal feature folder (`Features/Journal/...`), not in a cross-module/shared-kernel location.
- No other module is modified; the Marketing module's separate copy of the same pattern is explicitly out of scope for this change.

### FR-2: No change to public contracts
`GetJournalEntriesResponse` and `SearchJournalEntriesResponse` keep their existing shape (property names/types unchanged), and the two MediatR requests/endpoints keep their existing signatures.

**Acceptance criteria:**
- No changes to `GetJournalEntriesRequest`, `GetJournalEntriesResponse`, `SearchJournalEntriesRequest`, or `SearchJournalEntriesResponse`.
- No changes required to the generated OpenAPI/TypeScript client (response shape is unchanged).
- No controller/route changes.

## Non-Functional Requirements

### NFR-1: Performance
Negligible impact. The calculation is three arithmetic/comparison operations; extracting it to a helper (static method or local function) does not introduce measurable overhead.

### NFR-2: Maintainability
The formula must exist in exactly one place within the Journal module after this change, so a future correction (e.g. an edge case at an exact `PageSize` boundary) is a one-line fix that automatically applies to both `GetJournalEntries` and `SearchJournalEntries`.

## Data Model
No data model changes. This is a computation-only refactor over existing fields already present on both response DTOs:
- `TotalCount` (int, from the repository query result)
- `PageNumber` (int, from the request)
- `PageSize` (int, from the request)
- `TotalPages` (int, derived)
- `HasNextPage` (bool, derived)
- `HasPreviousPage` (bool, derived)

## API / Interface Design
No public API changes. Internally, add one shared helper in the Journal feature folder, e.g.:

```csharp
internal static class JournalPaginationCalculator
{
    public static (int TotalPages, bool HasNextPage, bool HasPreviousPage) Calculate(
        int totalCount, int pageNumber, int pageSize)
    {
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return (
            TotalPages: totalPages,
            HasNextPage: pageNumber * pageSize < totalCount,
            HasPreviousPage: pageNumber > 1);
    }
}
```

Exact placement (a new small file under `Features/Journal/`, e.g. alongside `Mapping/JournalEntryMapper.cs`, vs. a private static method on one handler referenced by the other) is an architecture decision — see Open Questions / hand off to the architecture phase. Both handlers assign the three returned values onto their respective response objects in place of the current inline expressions.

## Dependencies
None. No new packages, no external services. Pure internal C# refactor within `Anela.Heblo.Application.Features.Journal`.

## Out of Scope
- The Marketing module's separate (structurally identical but independently-maintained) pagination calculation — not touched by this change.
- Any change to pagination *behavior* (e.g. fixing a hypothetical edge case at an exact `PageSize` boundary) — this refactor preserves current behavior exactly; it does not change the formula.
- Any cross-module/shared-kernel pagination abstraction — the issue explicitly asks to keep this local to the Journal feature folder.
- Frontend changes — none needed, since response contracts are unchanged.

## Open Questions
None.

## Status: COMPLETE
