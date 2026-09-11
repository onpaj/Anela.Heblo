# Architecture Review: Deduplicate pagination metadata calculation in Journal handlers

## Skip Design: true

## Architectural Fit Assessment
This is a pure backend refactor confined to a single vertical slice (`Anela.Heblo.Application.Features.Journal`). It introduces no new public contracts, no new endpoints, no new dependencies, and touches no UI. It aligns cleanly with existing project conventions:

- The Journal feature folder already has a precedent for a small static helper class living beside its handlers: `Features/Journal/Mapping/JournalEntryMapper.cs` is a static class with a static `ToDto` method, referenced by both `GetJournalEntriesHandler` and `SearchJournalEntriesHandler` today. The new pagination helper should follow the exact same shape and placement convention.
- `development_guidelines.md` requires DTOs to live in `Contracts/` and forbids sharing DTOs across modules, but says nothing that restricts a private computational helper *within* a module — this change adds no DTO and crosses no module boundary, so it doesn't implicate any of the "Forbidden Practices" in that doc.
- A grep across the codebase confirms the identical `TotalPages`/`HasNextPage`/`HasPreviousPage` literal pattern is repeated in at least one other module (`Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs`) and appears structurally (property-wise, not code-wise) in a dozen more `*Response.cs` files across modules. The issue and spec explicitly scope this fix to Journal only — confirmed correct: a cross-module abstraction is out of scope here (it would require a `Xcc`/shared-kernel placement decision, module-boundary review, and touching unrelated modules, none of which this issue calls for). This review does not recommend expanding scope.

## Proposed Architecture

### Component Overview
```
Features/Journal/
├── Mapping/
│   └── JournalEntryMapper.cs            (existing, static class, ToDto)
├── Pagination/
│   └── JournalPaginationCalculator.cs   (NEW, static class, Calculate)
├── UseCases/
│   ├── GetJournalEntries/
│   │   └── GetJournalEntriesHandler.cs      -> calls JournalPaginationCalculator.Calculate(...)
│   └── SearchJournalEntries/
│       └── SearchJournalEntriesHandler.cs   -> calls JournalPaginationCalculator.Calculate(...)
```
No change to `Contracts/`, `JournalModule.cs`, controllers, or the domain/persistence layers. `IJournalRepository` is untouched.

### Key Design Decisions

#### Decision 1: Static helper class vs. local function vs. extension method
**Options considered:**
1. A `private static` local helper method duplicated as a local function inside each handler (as the issue's own "Suggested fix" snippet shows as one option) — rejected, because a local function inside `GetJournalEntriesHandler` that `SearchJournalEntriesHandler` needs to call would either require one handler to depend on the other (wrong direction, creates an arbitrary coupling between two independent use cases) or would still be two copies (one per handler) — this does not actually deduplicate anything.
2. A `static` method on one of the two handler classes, called by the other — rejected for the same reason: it makes one use case handler depend on another's implementation detail, which is not how vertical-slice siblings should relate, and is surprising to a future reader who doesn't expect `SearchJournalEntriesHandler` to reach into `GetJournalEntriesHandler`.
3. A new small `static` class (`JournalPaginationCalculator`) in its own file inside the Journal feature folder, called by both handlers — **chosen**.

**Chosen approach:** New file `Features/Journal/Pagination/JournalPaginationCalculator.cs`, a `public static class` (or `internal static class` — see Interfaces section) exposing one method, `Calculate(int totalCount, int pageNumber, int pageSize)`, returning a named tuple `(int TotalPages, bool HasNextPage, bool HasPreviousPage)`.

**Rationale:** Mirrors the existing `JournalEntryMapper` precedent exactly (a small static class, one clear method, referenced by both handlers as a peer utility, not by one handler depending on the other). Keeps the fix entirely inside the Journal feature folder as the issue requires. A named-tuple return keeps call-site code readable (`var (totalPages, hasNext, hasPrev) = ...` or property-style tuple deconstruction directly into the response initializer).

#### Decision 2: Folder placement — new `Pagination/` subfolder vs. reusing `Mapping/`
**Options considered:**
1. Put the new class inside the existing `Mapping/` folder alongside `JournalEntryMapper.cs` — simplest, no new folder, but `Mapping/` semantically means "DTO mapping," and pagination-metadata calculation isn't a mapping concern.
2. Create a new `Pagination/` subfolder — **chosen**. One file today; the name accurately describes its single responsibility and gives a home if this Journal-specific concern ever needs a companion (e.g., a request-validation helper for `PageNumber`/`PageSize`), without overloading `Mapping/`'s meaning.

**Chosen approach:** `Features/Journal/Pagination/JournalPaginationCalculator.cs`.

**Rationale:** Matches the existing convention of a topic-named subfolder under a feature (`Mapping/`, `UseCases/`) rather than dropping a loose file at the feature root. Low cost either way given this is a one-file addition — a developer implementing this may choose to inline it in `Mapping/` instead if they prefer one fewer folder; that deviation is acceptable and does not need to come back through review.

## Implementation Guidance

### Directory / Module Structure
- **New file:** `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs`
- **Modified files:**
  - `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs`
  - `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`
- **No changes** to `Contracts/`, `JournalModule.cs`, any controller, or any test project's structure (only test *content*, if any handler tests assert on these fields — see Prerequisites).

### Interfaces and Contracts
```csharp
namespace Anela.Heblo.Application.Features.Journal.Pagination
{
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
}
```
`internal` is sufficient and preferred: this is an Application-layer-internal implementation detail, not part of any module contract, and `internal` prevents it from ever being mistaken for a cross-module extension point (consistent with "DTOs are never shared or global" in spirit, even though this isn't a DTO). Both handlers already sit in the same assembly (`Anela.Heblo.Application`), so `internal` visibility is sufficient for both call sites.

Call sites (both handlers), replacing the three duplicated lines:
```csharp
var (totalPages, hasNextPage, hasPreviousPage) =
    JournalPaginationCalculator.Calculate(result.TotalCount, request.PageNumber, request.PageSize);

return new GetJournalEntriesResponse // or SearchJournalEntriesResponse
{
    Entries = entryDtos,
    TotalCount = result.TotalCount,
    PageNumber = request.PageNumber,
    PageSize = request.PageSize,
    TotalPages = totalPages,
    HasNextPage = hasNextPage,
    HasPreviousPage = hasPreviousPage
};
```

### Data Flow
Unchanged end-to-end. The only difference is *where* the three derived values are computed: previously inline in each `Handle` method; now inside `JournalPaginationCalculator.Calculate`, called from each `Handle` method with the same three inputs (`result.TotalCount`, `request.PageNumber`, `request.PageSize`) it already had in scope. Output values for any given input triple are identical to today's behavior — this is a structural refactor with zero behavior change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Divide-by-zero / unexpected result if `pageSize <= 0` is ever passed | Low | Out of scope — today's inline code has the exact same property (division by `request.PageSize` with no guard), so the refactor must reproduce this behavior exactly, not "fix" it. Do not add new validation as part of this change; that would be a silent scope/behavior expansion beyond what the issue asks for. |
| Accidentally changing one handler's math while "helping" during the extraction | Low | The helper's body must be a literal, mechanical lift of the existing three-line expression (same operators, same cast, same order) — no rewriting. Existing/updated unit tests for both handlers pin the current outputs for representative `(totalCount, pageNumber, pageSize)` triples so any accidental change is caught. |
| Reviewer/future reader expects this helper to be reusable across modules given the identical pattern elsewhere (Marketing) | Low | Keep the type `internal` and scoped to `Features/Journal/Pagination/`; do not name it generically (e.g. avoid `PaginationHelper` in a shared namespace) so it does not read as an invitation for other modules to reference it. |

## Specification Amendments
None required. The spec's "API / Interface Design" section already sketched this shape; this review pins down the exact namespace/folder (`Features/Journal/Pagination/JournalPaginationCalculator`) and visibility (`internal`) so the planner/developer do not need to make that call themselves.

## Prerequisites
- None (no migrations, no config, no infrastructure). Before implementation starts, confirm whether `GetJournalEntriesHandler` currently has direct unit test coverage for `TotalPages`/`HasNextPage`/`HasPreviousPage` (a scan found `SearchJournalEntriesHandlerTests.cs` but no `GetJournalEntriesHandlerTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Journal/`) — the planner should account for adding minimal coverage for both handlers' pagination fields as part of this task so the "no behavior change" acceptance criterion in the spec is actually verified, not just asserted.
