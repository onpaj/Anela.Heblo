# Specification: Extract `ComputeFromDate` helper in `GetCatalogDetailHandler`

## Summary
Refactor `GetCatalogDetailHandler.cs` to eliminate four duplicated `fromDate` computation blocks by extracting a single private helper method `ComputeFromDate(int monthsBack)`. The change consolidates the "full history vs. N months back" date logic in one place and removes a duplicated magic floor date (`2020-01-01`) from two locations. Pure internal refactor — no behavior change visible to callers, no API or schema change.

## Background
`Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` contains four private methods that compute the start date of a history window using the same two-branch logic governed by `CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD`:

- **Pattern A** (`GetManufactureCostHistoryFromMargins`, `GetMarginHistoryFromMargins`): assigns `fromDate = new DateTime(2020, 1, 1)` when the threshold is reached, otherwise `currentDate.AddMonths(-monthsBack)`.
- **Pattern B** (`GetPurchaseHistoryFromAggregate`, `GetManufactureHistoryFromAggregate`): same threshold check, but short-circuits with an early return of all records (no date filter), otherwise falls through to `currentDate.AddMonths(-monthsBack)`.

All four methods additionally duplicate `var currentDate = _timeProvider.GetUtcNow().Date;`.

The hardcoded floor date `new DateTime(2020, 1, 1)` is repeated in two methods. If it ever needs to change (or become configurable), every occurrence must be updated in lockstep — exactly the kind of duplication that invites drift. The two patterns also obscure that "full history" and "N months back" are simply two modes of the same concept.

This refactor was filed by the daily arch-review routine on 2026-05-29.

## Functional Requirements

### FR-1: Introduce `ComputeFromDate` helper
Add a single private instance method to `GetCatalogDetailHandler` that encapsulates both branches of the date-window logic.

```csharp
private DateTime ComputeFromDate(int monthsBack)
{
    if (monthsBack >= CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
        return new DateTime(2020, 1, 1);

    return _timeProvider.GetUtcNow().Date.AddMonths(-monthsBack);
}
```

**Acceptance criteria:**
- A single `ComputeFromDate(int monthsBack)` private instance method exists on `GetCatalogDetailHandler`.
- The method uses `CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD` for the branch condition.
- The method uses the injected `_timeProvider` for the current-date branch.
- The hardcoded floor date `new DateTime(2020, 1, 1)` appears exactly once in the file (inside the helper).

### FR-2: Replace Pattern A duplicates
Update `GetManufactureCostHistoryFromMargins` (lines 208–221) and `GetMarginHistoryFromMargins` (lines 249–260) to call `ComputeFromDate(monthsBack)` and use the returned value for their `.Where(... >= fromDate)` filter.

**Acceptance criteria:**
- Both methods invoke `ComputeFromDate(monthsBack)` and remove their local `currentDate` and `fromDate` computation blocks.
- The resulting filter behavior is identical to the pre-refactor behavior for every value of `monthsBack` (including the threshold boundary).
- Existing unit tests covering these handlers continue to pass without modification.

### FR-3: Unify Pattern B with the helper
Update `GetPurchaseHistoryFromAggregate` (lines 114–130) and `GetManufactureHistoryFromAggregate` (lines 173–186) to remove the early-return branch and use `ComputeFromDate(monthsBack)` with a uniform `.Where(p.Date >= fromDate)` filter.

When `monthsBack >= ALL_HISTORY_MONTHS_THRESHOLD`, `fromDate` is `2020-01-01`. Because the data window starts at or after this floor date in production, the filter `p.Date >= fromDate` naturally returns all records and is functionally equivalent to the prior early return.

**Acceptance criteria:**
- Both methods invoke `ComputeFromDate(monthsBack)` and remove their early-return branch plus their local `currentDate` and `fromDate` computation blocks.
- For `monthsBack >= ALL_HISTORY_MONTHS_THRESHOLD`, the records returned are identical to those returned by the pre-refactor early-return code path (verified against existing test fixtures and any production-shaped data used in tests).
- For `monthsBack < ALL_HISTORY_MONTHS_THRESHOLD`, behavior is unchanged.
- Existing unit tests continue to pass without modification.

### FR-4: Remove duplicated `currentDate` lines
Each of the four refactored methods must no longer declare its own `var currentDate = _timeProvider.GetUtcNow().Date;` line — the helper owns the time source. If `currentDate` is used elsewhere in the same method for an unrelated purpose, leave that usage in place but ensure no redundant duplicate declaration remains.

**Acceptance criteria:**
- Within the four methods, no `currentDate` variable is computed solely to derive `fromDate`.
- `_timeProvider.GetUtcNow().Date` is invoked at most once per call to the helper.

## Non-Functional Requirements

### NFR-1: Behavior preservation
This is a behavior-preserving refactor. Output (records returned by each of the four methods) must be byte-identical to the pre-refactor implementation for all reachable input combinations.

- Verified by running the full `dotnet test` suite touching the Catalog module and confirming no regressions.
- No callers, no DTOs, no SQL/EF query shapes, and no API responses change.

### NFR-2: Code quality
- The handler file becomes shorter and more cohesive (one definition of "full history vs. N months back" instead of four).
- The magic value `new DateTime(2020, 1, 1)` appears exactly once in the file.
- `dotnet build` passes with no new warnings.
- `dotnet format` reports no changes after the refactor.

### NFR-3: Performance
Negligible impact. The helper performs the same work as the inline code (one `_timeProvider` call, one comparison, one `AddMonths`). Method-call overhead is irrelevant at this scale.

## Data Model
No changes. This refactor does not touch entities, EF configuration, migrations, or any persisted state.

## API / Interface Design
No changes. `GetCatalogDetailHandler` exposes no new public surface; the helper is `private`. No MediatR contracts, MVC controllers, DTOs, or OpenAPI client outputs are affected.

## Dependencies
- `CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD` — already imported and used by the existing code.
- `_timeProvider` (`TimeProvider`) — already injected into the handler.
- No new packages, services, or feature flags.

## Out of Scope
- Extracting `new DateTime(2020, 1, 1)` to a named constant (e.g. `CatalogConstants.FULL_HISTORY_FLOOR_DATE`). Worth doing eventually, but the brief's suggested fix keeps it inline inside the helper, and the duplication is already resolved by FR-1. Tracked under Open Questions.
- Making the floor date configurable (appsettings / DI).
- Changing the semantics of `ALL_HISTORY_MONTHS_THRESHOLD` or the data-window contract.
- Refactoring other handlers or other "full history vs. N months back" patterns elsewhere in the codebase.
- Adding new unit tests beyond confirming existing tests pass. (If existing coverage of these four methods is insufficient, that is a separate concern.)
- Touching the `Pattern B` callers' downstream consumers — the early-return removal is internal to each method.

## Open Questions
None.

## Status: COMPLETE