### task: update-bank-statement-import-chart

**Goal:** Update `BankStatementImportChart.tsx` to use `item.date` as a `Date` directly (removing string parsing), rename `BankStatementImportStatisticsDto` references to `DailyBankStatementStatistics`, and add the `?? []` fallback where needed.

**Files to change:**
- `frontend/src/components/charts/BankStatementImportChart.tsx` — remove date string parsing; use `item.date!`; update type references from `BankStatementImportStatisticsDto` to `DailyBankStatementStatistics`; add `?? []` on `statistics` access if present in this file

**Implementation steps:**
1. Replace every `parseISO(item.date)` (or equivalent) with `item.date!`.
2. Rename all references to `BankStatementImportStatisticsDto` → `DailyBankStatementStatistics` (import and usage sites).
3. Remove any now-unused imports (e.g. `parseISO`, old DTO type) if they are no longer referenced elsewhere in the file.
4. If the component accesses `data.statistics` directly, add `?? []` to guard against `undefined`.

**Acceptance criteria:**
- No TypeScript errors in this file.
- No reference to `BankStatementImportStatisticsDto` remains.
- No `parseISO` call remains for `item.date` in this component.
- `npm run build` and `npm run lint` pass.

**Dependencies:** migrate-bank-statement-import-statistics-hook
