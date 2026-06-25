# Implementation: migrate-bank-statement-import-statistics-hook

## Summary

Replaced the raw `fetch` implementation in `useBankStatementImportStatistics` with a call to the generated `apiClient.analytics_GetBankStatementImportStatistics(...)` method. Removed duplicate local interfaces and added re-exports of generated types.

## Changes

### `frontend/src/api/hooks/useBankStatements.ts`

- **Removed** local `BankStatementImportStatisticsDto` interface (replaced by generated `DailyBankStatementStatistics`).
- **Removed** local `GetBankStatementImportStatisticsResponse` interface (replaced by generated equivalent).
- **Added** import of `GetBankStatementImportStatisticsResponse`, `DailyBankStatementStatistics`, `BankStatementDateType` from `../generated/api-client`.
- **Added** `export type { ... }` re-exports for all three generated types.
- **Kept** `GetBankStatementImportStatisticsRequest` interface unchanged with string date fields (required by FR-7).
- **Replaced** `queryFn` body: removed `URLSearchParams` construction, raw `fetch` call, and `(apiClient as any)` casts. Now calls `apiClient.analytics_GetBankStatementImportStatistics(startDate, endDate, dateType)` directly, converting string dates to `Date` objects via `new Date(...)` with null fallback for undefined values, and casting `dateType` to `BankStatementDateType | undefined`.
- `useBankStatementsList`, `useBankStatementImport`, and `useBankStatementAccounts` are untouched.

### `frontend/src/components/pages/BankStatementImportChart.tsx`

- **Added** `?? []` fallback at the `data={data.statistics ?? []}` call site to handle `statistics` being `DailyBankStatementStatistics[] | undefined` in the generated type.

## Acceptance criteria verification

- No TypeScript errors introduced (only pre-existing deprecation warnings in tsconfig).
- `GetBankStatementImportStatisticsRequest` still exported with string date fields.
- No `(apiClient as any)` usage remains for the `useBankStatementImportStatistics` queryFn.
- `DailyBankStatementStatistics`, `GetBankStatementImportStatisticsResponse`, `BankStatementDateType` are re-exported.
- Other hooks untouched.

## Commit

`e18632c` — `refactor(analytics): migrate useBankStatementImportStatistics to generated API client (#3333)`
