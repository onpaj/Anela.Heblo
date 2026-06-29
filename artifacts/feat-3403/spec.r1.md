# Specification: Fix DateTime.Now to DateTime.UtcNow in Catalog Background Refresh Task

## Summary

Two occurrences of `DateTime.Now` in `CatalogModule.RegisterBackgroundRefreshTasks` use the server's local clock instead of UTC, creating a timezone-dependent date boundary that can silently corrupt margin calculation windows by one calendar day. Replacing both with `DateTime.UtcNow` eliminates this risk, restores consistency with every other time-based computation in the Catalog module, and makes the background task testable with injected fake time.

## Background

The Catalog module runs a background task (`RefreshMarginData`) that recalculates product margins over a rolling historical window: from two years ago (or 2025-01-01, whichever is later) up to the end of the previous complete month. The intent of setting `dateTo` to "last month" is to exclude the current, still-incomplete month from margin calculations.

Every other date/time-sensitive path in the module — cost providers, margin services, and MediatR handlers — uses either `DateTime.UtcNow` or an injected `TimeProvider`. The two lines in `RegisterBackgroundRefreshTasks` are the only exception, a leftover inconsistency likely introduced before the UTC convention was established.

On Azure Web App for Containers the default OS timezone is UTC, so the bug is currently latent. It would become active if the container's timezone is ever changed, if the app is moved to a host with a non-UTC timezone, or if a developer runs a local test environment in a UTC+ offset: the `DateOnly` boundary can shift by a full calendar day, meaning the "incomplete current month" guard either includes a partial month's data or clips the last day of the previous month.

Because `DateTime.Now` is a static call with no injection point, the task also cannot be driven by a fake clock in automated tests.

## Functional Requirements

### FR-1: Replace DateTime.Now with DateTime.UtcNow on line 310

Replace the `twoYearsAgo` computation so it uses UTC time.

**Before:**
```csharp
var twoYearsAgo = DateOnly.FromDateTime(DateTime.Now.AddYears(-2));
```
**After:**
```csharp
var twoYearsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2));
```

**Acceptance criteria:**
- `DateTime.Now` no longer appears in `CatalogModule.cs`.
- `twoYearsAgo` is derived from `DateTime.UtcNow`.
- `dotnet build` passes with no warnings introduced.

### FR-2: Replace DateTime.Now with DateTime.UtcNow on line 313

Replace the `dateTo` computation so it uses UTC time.

**Before:**
```csharp
var dateTo = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1); // Current month is not accurate
```
**After:**
```csharp
var dateTo = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1); // Current month is not accurate
```

**Acceptance criteria:**
- `dateTo` is derived from `DateTime.UtcNow`.
- The existing inline comment is preserved unchanged.
- No other logic in the method is altered.

### FR-3: No other changes in scope

The fix is limited to the two literal substitutions described in FR-1 and FR-2. No refactoring of surrounding code, no introduction of `TimeProvider` injection into this helper, and no changes to any other file.

**Acceptance criteria:**
- `git diff` for this change shows only the two `DateTime.Now` → `DateTime.UtcNow` substitutions in `CatalogModule.cs`.
- All existing unit and integration tests that were passing before the change continue to pass.

## Non-Functional Requirements

### NFR-1: Performance

No performance impact. The change is a direct substitution of one static clock call for another; both are O(1) and sub-microsecond.

### NFR-2: Correctness / Timezone safety

After the fix, date boundary computation in `RegisterBackgroundRefreshTasks` is timezone-invariant. The `dateTo` guard correctly excludes the current calendar month under UTC regardless of host OS timezone setting.

### NFR-3: Consistency

After the fix, `DateTime.Now` has zero occurrences in the Catalog module. All time references in the module use `DateTime.UtcNow` or `TimeProvider`.

### NFR-4: Testability

This change is a prerequisite for any future test that needs to control the clock in `RegisterBackgroundRefreshTasks`. Replacing the static call is the minimum step; actual `TimeProvider` injection is out of scope for this fix (see Out of Scope).

## Data Model

No data model changes. The `Margins` property on `Product` entities is populated identically to before; only the date window passed to `GetMarginAsync` is guaranteed to be UTC-consistent.

## API / Interface Design

No API or UI changes. This is an internal background task computation.

## Dependencies

- `CatalogModule.cs` — `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, lines 310 and 313.
- No new dependencies are introduced.

## Out of Scope

- Introducing `TimeProvider` injection into `RegisterBackgroundRefreshTasks` or `RegisterRefreshTask`. That refactor would improve testability further but is a separate, larger change.
- Writing new unit tests for the background task clock behaviour. That is desirable but depends on `TimeProvider` injection which is excluded above.
- Auditing other modules for `DateTime.Now` usage. This fix addresses only the Catalog module finding.
- Any change to how `dateFrom` / `dateTo` are consumed by `GetMarginAsync` or stored in the database.

## Open Questions

None.

## Status: COMPLETE
