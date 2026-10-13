# Design: Unit test coverage for MarginsChart's monthly data mapping

## Component Design

### `MarginsChart.utils.ts` (new)
A colocated, non-React utility module in `frontend/src/components/catalog/detail/tabs/MarginsTab/`, sitting alongside `MarginsChart.tsx`. Its sole responsibility is the pure data-mapping logic currently embedded in `MarginsChart`'s `mapMarginDataToMonthlyArrays`:

- Builds eight lookup `Map<string, number>`s (`m0PercentageMap`, `m1PercentageMap`, `m2PercentageMap`, `m3PercentageMap`, `m0CostLevelMap`, `m1CostLevelMap`, `m2CostLevelMap`, `m3CostLevelMap`) keyed by `"{year}-{month}"`, from a `MarginHistoryDto[]` input, skipping any record whose date falls in the current calendar month/year.
- Fills eight fixed-length-12 output arrays for the trailing 12 months (excluding the current month), applying the year-boundary correction (decrement year / add 12 to month when `currentMonth - monthsBack <= 0`) and falling back to `0` for any month with no matching record.
- Takes an explicit, optional `now: Date` parameter (default `new Date()`) so callers — in production, `MarginsChart`; in tests, the new test file — control "current date" without any global time mocking.
- Has no dependency on React, `react-chartjs-2`, or any rendering machinery; its only import is the `MarginHistoryDto` type.

**Responsibility boundary:** this module owns *only* the year/month bucketing and lookup-with-fallback logic. It does not own chart configuration, styling, labels, or the `hasM0M2Data`/`hasData` presence checks — those stay in `MarginsChart.tsx`.

### `MarginsChart.tsx` (modified)
Unchanged behavior and unchanged external contract (`MarginsChartProps` — `marginHistory`, `journalEntries` — is untouched). Internally:
- Removes the inline `mapMarginDataToMonthlyArrays` function body.
- Imports `mapMarginDataToMonthlyArrays` from `./MarginsChart.utils` and calls it with no `now` argument (`mapMarginDataToMonthlyArrays(marginHistory)`), preserving today's runtime behavior exactly (defaults to `new Date()`).
- `generateMonthLabelsExcludingCurrent` and all rendering/chart-configuration logic (styling, dataset construction, `chartOptions`, the `hasData` conditional render) are unchanged and stay in this file — they are not part of this task.

### `MarginsChart.utils.test.ts` (new)
A colocated Jest test file in `MarginsTab/__tests__/`, following the existing pattern of `MarginsSummary.test.tsx` in the same directory. It imports `mapMarginDataToMonthlyArrays` directly from `../MarginsChart.utils` and calls it with fixture `MarginHistoryDto[]` arrays and fixed `now` dates — no component rendering, no `react-chartjs-2`/Chart.js mocking required. Covers, per the spec's FR-1 through FR-4:
- January run: full previous-year rollback across all 12 slots.
- Mid-year run: mixed same-year/previous-year slots, asserted slot-by-slot.
- Current-month exclusion: a same-year-and-month record contributes nothing; a same-month-different-year record is not excluded.
- Missing-data fallback: unmatched months are `0` across all 8 arrays; an entirely empty input yields all zeros.

## Data Schemas

No new or changed data schemas. Both the input type (`MarginHistoryDto`, from `frontend/src/api/generated/api-client`, generated from the backend OpenAPI spec — unchanged) and the output shape (the 8-array object literal returned by `mapMarginDataToMonthlyArrays`) are identical to what exists today; extraction moves the code, it does not reshape data.

```typescript
// Input (existing, unchanged)
interface MarginHistoryDto {
  date?: Date;
  m0?: { percentage?: number; amount?: number; costLevel?: number; costTotal?: number };
  m1?: { percentage?: number; amount?: number; costLevel?: number; costTotal?: number };
  m2?: { percentage?: number; amount?: number; costLevel?: number; costTotal?: number };
  m3?: { percentage?: number; amount?: number; costLevel?: number; costTotal?: number };
  // ...other existing fields, untouched
}

// Output (existing shape, now the extracted function's return type)
interface MonthlyMarginArrays {
  m0PercentageData: number[];   // length 12
  m1PercentageData: number[];   // length 12
  m2PercentageData: number[];   // length 12
  m3PercentageData: number[];   // length 12
  m0CostLevelData: number[];    // length 12
  m1CostLevelData: number[];    // length 12
  m2CostLevelData: number[];    // length 12
  m3CostLevelData: number[];    // length 12
}
```
