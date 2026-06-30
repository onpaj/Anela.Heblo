# Code Review: migrate-bank-statement-import-statistics-hook

## Summary
The implementation correctly removes the `(apiClient as any)` bypass from `useBankStatementImportStatistics` and replaces it with a typed call to the generated client method. All required re-exports are present, the `GetBankStatementImportStatisticsRequest` interface is unchanged, `staleTime` is preserved at 5 minutes, and the other three hooks are untouched. The `BankStatementImportChart.tsx` change adds a defensive `?? []` fallback that is correct and harmless.

## Review Result: PASS

### task: migrate-bank-statement-import-statistics-hook
**Status:** PASS

## Overall Notes
All five acceptance criteria are met:

1. No `(apiClient as any)` remains in `useBankStatementImportStatistics` — the hook now calls `apiClient.analytics_GetBankStatementImportStatistics(...)` on the typed client instance.
2. `GetBankStatementImportStatisticsRequest` is still exported with `startDate?: string`, `endDate?: string`, `dateType?: string` fields unchanged.
3. `GetBankStatementImportStatisticsResponse`, `DailyBankStatementStatistics`, and `BankStatementDateType` are all re-exported via `export type { ... }` on line 5.
4. `useBankStatementsList`, `useBankStatementImport`, and `useBankStatementAccounts` are untouched and retain their existing `(apiClient as any)` patterns (those are out of scope for this task).
5. `staleTime` is `5 * 60 * 1000` (5 minutes), unchanged.

One observation for awareness (not a blocking issue): the date parameters are passed as `null` when undefined (`request.startDate ? new Date(request.startDate) : null`). This is consistent with the implementation summary and is appropriate if the generated client signature accepts `Date | null`; it correctly signals "no date filter" to the API rather than passing `undefined` which some generated clients handle differently.

**Status:** PASS
