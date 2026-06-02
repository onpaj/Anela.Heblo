# Specification: Extract `ResolveDateRange` Helper in `GiftPackageManufactureService`

## Summary
Eliminate a duplicated 5-line date-range/`dailySales` defaulting block in `GiftPackageManufactureService` by extracting a private helper method. The change is a pure refactor: behavior, public API, and external contracts remain identical.

## Background
`GiftPackageManufactureService` contains two consumer methods — `GetAvailableGiftPackagesAsync` (lines ~55–63) and `GetGiftPackageDetailAsync` (lines ~112–118) — that each contain a verbatim copy of the following logic:

```csharp
var actualToDate = toDate ?? _timeProvider.GetUtcNow().DateTime;
var actualFromDate = fromDate ?? actualToDate.AddYears(-1);
var daysDiff = Math.Max((actualToDate - actualFromDate).Days, 1);
// ...
var totalSalesInPeriod = (decimal)product.GetTotalSold(actualFromDate, actualToDate) * salesCoefficient;
var dailySales = totalSalesInPeriod / daysDiff;
```

The defaulting rule ("if `toDate` is null use now; if `fromDate` is null use one year before `toDate`") and the `daysDiff` floor of 1 are policy decisions that should live in one place. As of today `CreateManufactureAsync` (line ~200) already routes one consumer through the other, but the duplication still forces any future change (e.g., switching the lookback window from 1 year to 6 months, or changing the day-count rounding) to be applied in two locations — a known source of inconsistency.

This refactor was filed by the daily architecture-review routine on 2026-05-28 and is purely an internal cleanup; it ships no user-visible behavior change.

## Functional Requirements

### FR-1: Introduce `ResolveDateRange` private helper
Add a private instance method on `GiftPackageManufactureService` with the following signature and semantics:

```csharp
private (DateTime From, DateTime To, int Days) ResolveDateRange(DateTime? fromDate, DateTime? toDate)
{
    var to = toDate ?? _timeProvider.GetUtcNow().DateTime;
    var from = fromDate ?? to.AddYears(-1);
    return (from, to, Math.Max((to - from).Days, 1));
}
```

**Acceptance criteria:**
- The method is declared `private` (not `internal` or `public`); it is an implementation detail of the service.
- The method is non-static and uses the existing injected `_timeProvider` for "now" — it does not call `DateTime.UtcNow` directly.
- The tuple element order is `(From, To, Days)` and tuple elements are named so call sites can use property-style access.
- `Days` is computed as `Math.Max((To - From).Days, 1)` — the floor of 1 day must be preserved exactly to avoid division-by-zero in `dailySales`.
- Defaulting precedence is preserved: `toDate` is resolved first; `fromDate` then defaults relative to the resolved `to`, not relative to "now".
- The helper is placed alongside other private helpers in the same file/class; no new file, namespace, or interface is introduced.

### FR-2: Replace duplicated logic in `GetAvailableGiftPackagesAsync`
Replace the existing three-line date-resolution block (the lines that compute `actualToDate`, `actualFromDate`, `daysDiff`) with a single call to `ResolveDateRange`. The subsequent `totalSalesInPeriod` and `dailySales` computations remain inline (they depend on per-product `salesCoefficient` and cannot be hoisted into the helper).

**Acceptance criteria:**
- The three lines computing `actualToDate`, `actualFromDate`, and `daysDiff` are removed.
- A single statement `var (actualFromDate, actualToDate, daysDiff) = ResolveDateRange(fromDate, toDate);` (or equivalent named-tuple destructuring) replaces them.
- All downstream references inside the method continue to use the same variable names so no other lines need editing.
- The `totalSalesInPeriod` / `dailySales` expressions are unchanged.
- Method signature, return type, parameter list, and all other behavior are unchanged.

### FR-3: Replace duplicated logic in `GetGiftPackageDetailAsync`
Apply the same replacement as FR-2 in `GetGiftPackageDetailAsync`.

**Acceptance criteria:**
- Same as FR-2, applied to `GetGiftPackageDetailAsync`.
- The relationship between `GetGiftPackageDetailAsync` and `GetAvailableGiftPackagesAsync` (the latter being called from `CreateManufactureAsync` at ~line 200) is unchanged — this refactor does not consolidate the two methods.

### FR-4: No public API or behavioral change
The refactor must be observably a no-op to all callers.

**Acceptance criteria:**
- No public, internal, or protected member of `GiftPackageManufactureService` changes signature, return type, or visibility.
- No DI registration, interface, controller, MediatR handler, or DTO is touched.
- Existing unit and integration tests for `GiftPackageManufactureService` (covering both `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync`) continue to pass without modification.
- If existing tests use `_timeProvider` (mocked `TimeProvider`/`ITimeProvider`) to control "now", their assertions about resolved date ranges and `dailySales` must continue to pass with identical values.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change. The helper is a synchronous, allocation-light method (one value-tuple return). Inlining by the JIT is acceptable but not required.

### NFR-2: Security
Not applicable — internal refactor, no change to authentication, authorization, or data exposure.

### NFR-3: Maintainability
After the refactor, any future change to the date-range defaulting policy or the `daysDiff` floor must require editing exactly one location (the helper). This is the primary success criterion of the work.

### NFR-4: Build & validation gates
- `dotnet build` succeeds with no new warnings.
- `dotnet format` reports no diffs.
- All tests touching `GiftPackageManufactureService` pass locally.
- No new lint or analyzer findings are introduced.

## Data Model
None. No persisted entities, DTOs, contracts, or migrations are involved.

## API / Interface Design
None. No HTTP endpoint, MediatR request/response, or event contract is added, removed, or modified. The refactor is internal to a single application-layer service class.

## Dependencies
- Existing injected `TimeProvider` (or project-local equivalent — referred to in the brief as `_timeProvider`) continues to be the source of "now".
- No new NuGet packages, no new internal modules, no cross-feature references.

## Out of Scope
- Consolidating `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` into a single method or making one delegate to the other beyond what already exists.
- Hoisting `totalSalesInPeriod` or `dailySales` (which depend on `product` and `salesCoefficient`) into the helper.
- Changing the default lookback window (1 year), the `daysDiff` floor (1), or the rounding/truncation behavior of `(to - from).Days`.
- Replacing the value-tuple return with a named record/struct type.
- Promoting the helper to a shared utility class, extension method, or reusable type — it remains private to the service.
- Renaming `_timeProvider` or altering DI registration.
- Touching unrelated logic in `GiftPackageManufactureService`, including other methods, error handling, or logging.
- Adding new tests beyond what is required to keep the existing suite green. (If existing coverage already verifies the date-defaulting policy via either consumer method, no new tests are required.)

## Open Questions
None.

## Status: COMPLETE