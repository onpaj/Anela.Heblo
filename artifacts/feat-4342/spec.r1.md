# Specification: Unit test coverage for MarginsChart's monthly data mapping

## Summary
`MarginsChart.tsx` contains a pure data-mapping function, `mapMarginDataToMonthlyArrays`, that builds the 12-month arrays fed to the margin/cost chart, including a year-boundary correction, a current-month exclusion filter, and a fallback-to-zero for months with no data. None of this logic is covered by tests today (1.3% line coverage on the file). This spec covers adding focused unit tests for that mapping logic — no production code behavior is intended to change.

## Background
`mapMarginDataToMonthlyArrays` (lines ~41-124) walks `marginHistory` records into per-metric `Map<string, number>` lookups keyed by `"{year}-{month}"`, then fills 12 fixed-size output arrays for the trailing 12 months (excluding the current month) by computing `adjustedMonth = currentMonth - monthsBack` and, when that value is `<= 0`, decrementing `adjustedYear` and adding 12 to `adjustedMonth` (lines 97-101). A record whose date falls in the current calendar month is skipped entirely (line 71). Any month with no matching record in the lookup falls back to `0` for all eight arrays (lines 104-111, via `.get(key) || 0`).

Because `now = new Date()` is read directly inside the function, the year-boundary branch is only reachable when the code actually runs in the first ~11 months' worth of "months back" range that crosses January — concretely, when `currentMonth - monthsBack <= 0`. Since `monthsBack` ranges from 12 down to 1 across the 12 loop iterations, this branch is *always* exercised in every real invocation except when `currentMonth` is 12 (December) — i.e. it is far from an edge case, it is normal-path behavior for 11 months of the year, but it is currently completely unverified. An off-by-one here (e.g. using `< 0` instead of `<= 0`, or adding 11 instead of 12) would silently shift every pre-boundary month's data by one slot on the chart, which is the exact bug class this issue is filed to catch.

Today the function is defined inline inside the `MarginsChart` component and is not exported, so it cannot be unit-tested in isolation without either (a) extracting it into a standalone, exported pure function, or (b) testing it indirectly by rendering `MarginsChart` and asserting on the resulting chart data. Because the function's only effect is to compute the data arrays that flow into `chartData.datasets[...].data`, and because chart datasets are only present in the DOM/props when `hasM0M2Data` is true, an indirect (render-based) test would need to inspect chart props passed to the mocked `Chart` component from `react-chartjs-2`, matching the existing pattern in `frontend/src/components/charts/__tests__/BankStatementImportChart.test.tsx` and `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsSummary.test.tsx` (render + inspect, with `jest.mock` for chart libraries that don't behave well in jsdom).

Both approaches are viable; the architect phase should pick one (see Open Questions) since it changes the shape of the code touched. This spec assumes extraction is the preferred path because it directly unit-tests the exact function named in the issue, needs no chart-library mocking, and is the lowest-risk way to pin down month-index math precisely — but flags it as an assumption for architect review.

## Functional Requirements

### FR-1: Year-boundary correction is tested when the run date is in January
When "now" is January (month = 1) and the loop computes months 1 through 12 back, the resulting `adjustedYear`/`adjustedMonth` pairs must correctly roll back into the previous calendar year for all `monthsBack` values where `currentMonth - monthsBack <= 0`, and must land in the current year only for `monthsBack` values where the subtraction is positive.

**Acceptance criteria:**
- With current date fixed to January of a given year (e.g. `2026-01-15`), the 12 generated month keys (in loop order, `i = 0..11`) cover the previous December back through the previous January, i.e. `2025-2, 2025-3, ..., 2025-12, 2026-1` is wrong — the correct expected sequence for `currentMonth = 1` is 12 keys reaching back from `2025-2` through `2026-1`... (test must derive the expected keys the same way a human would reason about "12 months ending at, but excluding, the current month," not by re-deriving the implementation's arithmetic) — concretely: for `currentMonth=1, currentYear=Y`, `monthsBack` goes 12→1, producing months `Y-1-1 (Jan of Y-1)` ... up to `Y-1-12 (Dec of Y-1)`. The test must assert these are all in year `Y-1`, with none incorrectly left in year `Y`.
- A record dated in December of the previous year (which is the month immediately before January) is placed in the last slot (`i = 11`) of the output arrays, and a record dated in January of the previous year is placed in the first slot (`i = 0`).
- No off-by-one: a record dated in January of the *current* year (i.e. two years before the "12 months back" boundary) must NOT appear anywhere in the output arrays for this scenario, since it falls outside the 12-month window when current month is January (12 months back from January covers only the prior year).

### FR-2: Mid-year case has no year crossover
When "now" is a mid-year month (e.g. July, month = 7), none of the 12 computed `adjustedYear` values differ from `currentYear` for the `monthsBack` values that keep `adjustedMonth > 0`, and the boundary branch is exercised only for the months that do cross back into the previous year (`monthsBack` values 7 through 12, corresponding to July of the previous year through December of the previous year — i.e. this case still partially crosses the boundary for a mid-year month with `monthsBack > currentMonth`, since the 12-month lookback window always spans two calendar years unless `currentMonth = 12`). The test must cover both the same-year portion and the boundary-crossing portion within a single mid-year scenario, verifying the year assigned to each of the 12 slots individually.

**Acceptance criteria:**
- With current date fixed to a mid-year month (e.g. `2026-07-15`), assert the full 12-slot year/month sequence slot-by-slot (not just spot-checking one or two), confirming which slots fall in the previous year and which fall in the current year.
- A record dated in the current year, in a month between `1` and `currentMonth - 1` inclusive, is placed in the correct current-year slot.
- A record dated in the previous year, in a month greater than `currentMonth`, is placed in the correct previous-year slot.

### FR-3: Current-month exclusion filter is tested
A `marginHistory` record whose `date` falls within the current calendar month and year is excluded from all eight output arrays, regardless of its `m0`/`m1`/`m2`/`m3` values.

**Acceptance criteria:**
- Given a record dated in the current month/year with non-zero `m0`-`m3` percentage and costLevel values, and no other records, all 12 slots of all 8 output arrays are `0` (the record contributes nothing).
- A record dated in the current month of a *different* year (i.e. same month number, but not `currentYear`) is NOT excluded by this filter and is correctly placed in its year-appropriate slot (this distinguishes the "current month" check from a "this month-of-year" check, confirming the filter compares both year and month, not month alone).

### FR-4: Missing-data fallback to zero is tested
A slot (month) in the 12-month window for which `marginHistory` has no matching record falls back to `0` in all eight metric arrays for that slot, without affecting the values of adjacent slots that do have data.

**Acceptance criteria:**
- Given a `marginHistory` array with data for only some of the 12 months in the window (e.g. every other month), the slots with no matching record are exactly `0` across all 8 arrays, and the slots with matching records reflect that record's `m0`-`m3` `percentage`/`costLevel` values (falling back to `0` only for a metric that is itself `undefined` on the record, per the existing `record.m0?.percentage || 0` pattern — this nullish-metric fallback is in scope for the same test since it shares the `|| 0` mechanism, though it is not separately called out in the issue).
- An empty `marginHistory` array (`[]`) results in all 8 arrays being 12 zeros each.

## Non-Functional Requirements

### NFR-1: Determinism
Tests must not depend on the actual wall-clock date. The current date must be controlled deterministically (e.g. via Jest fake timers / `jest.useFakeTimers().setSystemTime(...)`, or by injecting a fixed `Date` if the function is refactored to accept one) so the suite passes identically regardless of when CI runs it, including when CI itself runs in January.

### NFR-2: No behavior change
This is a test-only change. `mapMarginDataToMonthlyArrays`'s logic must not be altered to fix the "bug" it doesn't have — the current arithmetic (verified by manual trace above) is correct; the goal is coverage, not a fix. If extraction into a standalone module is chosen, the extracted function's behavior must be byte-for-byte identical to the inline version (same inputs → same outputs), verified by the new tests themselves passing against the extracted code unmodified.

## Data Model
No new data model. Tests exercise `mapMarginDataToMonthlyArrays`'s existing inputs/outputs:
- Input: `MarginHistoryDto[]` (imported from `../../../../../api/generated/api-client`), each with `date`, and optional `m0`/`m1`/`m2`/`m3` objects with `percentage` and `costLevel` fields.
- Output: an object with 8 fixed-length (12) `number[]` arrays: `m0PercentageData`, `m1PercentageData`, `m2PercentageData`, `m3PercentageData`, `m0CostLevelData`, `m1CostLevelData`, `m2CostLevelData`, `m3CostLevelData`.

## API / Interface Design
No API changes. If extraction is chosen (see Open Questions), the interface change is: export `mapMarginDataToMonthlyArrays` as a named, testable pure function taking `marginHistory: MarginHistoryDto[]` and (if refactored per NFR-1) an optional injectable `now: Date` parameter defaulting to `new Date()`, callable independently of rendering `MarginsChart`.

## Dependencies
- Jest + `@testing-library/react` (already used elsewhere in the frontend test suite, e.g. `MarginsSummary.test.tsx`).
- Jest fake timers (`jest.useFakeTimers`) for controlling "now" — already a supported Jest feature, no new dependency.
- No new npm packages required.

## Out of Scope
- Any change to `MarginsChart`'s rendering, styling, chart configuration, or the `generatePointStyling`/`generateTooltipCallback` helpers.
- Fixing any bug in the mapping logic — none was found; this is a coverage-only change.
- Testing `MarginsChart`'s overall rendering (the `hasData` branch, chart type selection, dataset construction beyond the data arrays) beyond what is incidentally needed to reach the mapping logic, if the indirect (render-based) testing approach is chosen instead of extraction.
- Raising the file's line coverage to the full 60% threshold in general — only the specifically flagged lines (71, 97-101, 104-111) and their immediately surrounding logic are in scope. (In practice, testing `mapMarginDataToMonthlyArrays` thoroughly is very likely to bring the whole function, and probably the file, close to or above threshold as a side effect, but that is not itself a requirement.)

## Open Questions
None. (Decision made: extract `mapMarginDataToMonthlyArrays` into a standalone, exported, unit-testable pure function rather than testing it indirectly through a rendered `MarginsChart` + mocked `Chart` component. This is consistent with the codebase's existing convention of externalizing chart-support logic into directly-tested modules, e.g. `ChartHelpers.tsx` / `ChartHelpers.test.ts`, is lower-mocking-overhead, and most directly targets the exact function named in the issue. The architect should confirm the target module location and export shape during architecture review, since that is an implementation-structure decision rather than a requirements question.)

## Status: COMPLETE
