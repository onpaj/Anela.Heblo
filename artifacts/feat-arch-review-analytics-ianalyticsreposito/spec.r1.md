# Specification: Remove Unused `GetGroupMarginTotalsAsync` from `IAnalyticsRepository`

## Summary
Delete the speculative `GetGroupMarginTotalsAsync` method from `IAnalyticsRepository` and its implementation in `AnalyticsRepository`, including the duplicated private `GetGroupKey` helper. The method has no callers, carries a TODO acknowledging it is not production-ready, and duplicates logic that already lives in `MarginCalculator`.

## Background
A daily architecture review on 2026-05-28 identified that `IAnalyticsRepository.GetGroupMarginTotalsAsync` is dead code. A repository-wide grep confirms the method is referenced only by its interface declaration and implementation — no handler, service, controller, or test consumes it.

Three concrete problems:
1. **YAGNI / speculative API.** The implementation contains the comment `// TODO: This would be optimized SQL query in real database implementation` — it was added in anticipation of a future need that never materialized.
2. **Interface bloat / ISP violation.** Any future implementation or test double of `IAnalyticsRepository` must implement a method nobody calls.
3. **Duplication.** The private `AnalyticsRepository.GetGroupKey` helper is byte-for-byte identical to `MarginCalculator.GetGroupKey`. Adding a new `ProductGroupingMode` value would require updating both copies, and the two will silently drift if only one is updated.

Removing the dead surface now eliminates the duplication risk and trims the interface to what is actually used. If an optimized aggregation query is genuinely needed later, it can be reintroduced alongside a real caller.

## Functional Requirements

### FR-1: Remove `GetGroupMarginTotalsAsync` from `IAnalyticsRepository`
Delete the method declaration at `backend/.../Infrastructure/IAnalyticsRepository.cs:26` (the `Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(...)` signature).

**Acceptance criteria:**
- `IAnalyticsRepository` no longer declares `GetGroupMarginTotalsAsync`.
- A repository-wide search for `GetGroupMarginTotalsAsync` returns no matches in any `.cs` file under `backend/`.

### FR-2: Remove `GetGroupMarginTotalsAsync` implementation from `AnalyticsRepository`
Delete the method body at `backend/.../Infrastructure/AnalyticsRepository.cs` (lines 41–68 per the brief), including the TODO comment.

**Acceptance criteria:**
- `AnalyticsRepository` no longer contains `GetGroupMarginTotalsAsync`.
- The TODO comment about "optimized SQL query in real database implementation" is gone.
- No `using` directives become unused as a side effect; if they do, remove them.

### FR-3: Remove the duplicated private `GetGroupKey` helper from `AnalyticsRepository`
Delete the private `GetGroupKey` method at `backend/.../Infrastructure/AnalyticsRepository.cs` (lines 79–88 per the brief). Do **not** touch `MarginCalculator.GetGroupKey` — it remains the single source of truth.

**Acceptance criteria:**
- `AnalyticsRepository.GetGroupKey` no longer exists.
- `MarginCalculator.GetGroupKey` is unchanged.
- No other code references the removed helper.

### FR-4: Verify nothing else depended on the removed surface
After deletion, confirm the build passes cleanly and no test references the removed method.

**Acceptance criteria:**
- `dotnet build` succeeds with no errors or new warnings.
- `dotnet format` reports no formatting issues introduced by this change.
- No test file in the solution references `GetGroupMarginTotalsAsync`.
- The full backend test suite passes.

## Non-Functional Requirements

### NFR-1: Surgical scope
The change must be a pure deletion. No refactoring of adjacent code, no renames, no comment cleanups, no signature changes elsewhere. Every removed line must trace to FR-1, FR-2, or FR-3.

### NFR-2: No behavior change
Because the removed method has no callers, observable application behavior — API responses, analytics calculations, margin grouping — must be identical before and after the change.

### NFR-3: Style consistency
Remaining code in `AnalyticsRepository.cs` and `IAnalyticsRepository.cs` retains its existing formatting and conventions. Run `dotnet format` if needed.

## Data Model
No data model changes. No entities, DTOs, database schemas, or migrations are affected.

## API / Interface Design

**Interface change (internal only):**
- `IAnalyticsRepository` loses one method. The interface is internal to the backend; no public HTTP API, OpenAPI contract, or generated TypeScript client is affected.

**No external API impact:**
- No controller endpoints change.
- No MediatR request/handler shapes change.
- No OpenAPI regeneration needed.

## Dependencies
- None added or removed.
- `MarginCalculator.GetGroupKey` (in `Services/MarginCalculator.cs`) becomes the sole implementation of the grouping-key logic. No code change is required there, but future maintainers should treat it as the canonical implementation.

## Out of Scope
- Implementing a real optimized SQL aggregation query for group margin totals. (Defer until a real caller appears.)
- Extracting `GetGroupKey` into a shared utility or extension method. The single remaining copy in `MarginCalculator` is sufficient; introducing an abstraction now would be premature.
- Any other cleanup of `AnalyticsRepository` or `MarginCalculator` beyond the three deletions specified.
- Changes to `ProductGroupingMode`, `AnalyticsProductType`, or any other analytics-domain type.
- Adding new tests. (Dead code being deleted has no tests to migrate; no new behavior is introduced.)

## Open Questions
None.

## Status: COMPLETE