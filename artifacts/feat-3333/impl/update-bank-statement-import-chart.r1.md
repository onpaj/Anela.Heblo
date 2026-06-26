# Implementation Summary: update-bank-statement-import-chart

## Task
Refactor `BankStatementImportChart.tsx` to:
- Use `item.date!` (a `Date`) directly instead of `parseISO(item.date)` (string parsing)
- Rename `BankStatementImportStatisticsDto` references to `DailyBankStatementStatistics`
- Remove unused imports

## Changes Made

### `frontend/src/components/charts/BankStatementImportChart.tsx`

1. **Import line updated**: Removed `parseISO` from `date-fns` import; replaced `BankStatementImportStatisticsDto` import with `DailyBankStatementStatistics` from `../../api/hooks/useBankStatements`.

2. **Props interface updated**: `data: BankStatementImportStatisticsDto[]` → `data: DailyBankStatementStatistics[]`.

3. **`ChartDataPoint.date` type updated**: `string` → `Date` (to match the generated type's field).

4. **`data.map` transform updated**: Removed `const parsedDate = parseISO(item.date)` and replaced all three usages of `parsedDate` / `parseISO(item.date)` with direct `item.date!`.

5. **Debug log updated**: `parseISO(d.date)` → `d.date` (since `d.date` is now a `Date`).

6. **Tooltip updated**: Both `parseISO(data.date)` calls replaced with `data.date`.

## Acceptance Criteria Status

- No reference to `BankStatementImportStatisticsDto` remains in any touched file. **PASS**
- No `parseISO(item.date)` calls remain in the chart component. **PASS**
- TypeScript compiles without errors (only pre-existing deprecation warnings from tsconfig). **PASS**

## Notes

- The pages file (`frontend/src/components/pages/BankStatementImportChart.tsx`) already had `?? []` applied by a prior task and did not need further changes.
- `DailyBankStatementStatistics` is already re-exported from `../../api/hooks/useBankStatements`, matching the task requirement.
- The generated `DailyBankStatementStatistics.date` is typed as `Date?` and is deserialized via `new Date(...)` in the generated client, so no runtime parsing is needed in the chart.
