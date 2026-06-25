# PR Context

- **PR**: #3340 — #3333: migrate analytics hooks to generated API client
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3340
- **Branch**: `feature/3333-Arch-Review-Analytics-Two-Frontend-Hooks-Bypass-Ge` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +1095 / -128 across 32 files
- **Absorbed**: backmerged with `main` (clean), test fix applied, all tests passing, pushed

## Absorb notes

- Backmerge of `origin/main` was clean (no conflicts).
- `npm run build` passes. `npm run lint` has 146 pre-existing errors (identical on `origin/main`) — none in PR-touched files; PR introduces no new lint errors.
- **Test fix**: `useInvoiceImportStatistics.test.ts` still mocked the old `http.fetch` path while the migrated hook now calls `apiClient.analytics_GetInvoiceImportStatistics(...)` directly. Updated the test to mock the generated client method and assert on `('InvoiceDate', null)` / `('LastSyncTime', 7)` style arguments. Committed as `fix: update useInvoiceImportStatistics test to mock generated client method`.
- No other test files reference the migrated hooks/types or removed interfaces.

## Description

Closes #3333

## What the issue was

Two React Query hooks in the Analytics frontend module — `useInvoiceImportStatistics` and `useBankStatementImportStatistics` — bypassed the generated OpenAPI TypeScript client by using raw `(apiClient as any).http.fetch(...)` calls and redefining types already exported by the generated client. This meant backend contract changes (field renames, type changes) would silently drift rather than producing compile-time errors, and auth header injection / retry logic provided by the generated client was being bypassed.

## How it was fixed

Replaced the raw fetch calls with typed generated-client method invocations in both hooks, removed the duplicate hand-written interfaces, and re-exported the generated types from the hook files so chart consumers needed no import-path changes.

### Changes
- `frontend/src/api/hooks/useInvoiceImportStatistics.ts` — replaced `(apiClient as any).http.fetch(...)` with `apiClient.analytics_GetInvoiceImportStatistics(...)`; removed duplicate `DailyInvoiceCount` and `InvoiceImportStatisticsResponse` interfaces; re-exported generated equivalents
- `frontend/src/api/hooks/useBankStatements.ts` — replaced raw fetch in `useBankStatementImportStatistics` with `apiClient.analytics_GetBankStatementImportStatistics(...)`; removed duplicate interfaces; retained `GetBankStatementImportStatisticsRequest` with string dates (converting to `Date` inside `queryFn`); other hooks in the file untouched
- `frontend/src/components/charts/InvoiceImportChart.tsx` — updated `date: string` → `date: Date` (the generated type); replaced `parseISO(item.date)` with `item.date!`; added `?? 0` / `?? false` fallbacks for optional generated type fields
- `frontend/src/components/charts/BankStatementImportChart.tsx` — same `date` type update + `parseISO` removal; renamed `BankStatementImportStatisticsDto` → `DailyBankStatementStatistics`; added `?? 0` fallbacks
- `frontend/src/components/pages/automation/InvoiceImportStatistics.tsx` — added `?? []` / `?? 0` fallbacks for optional fields from generated response type; `?? 0` on summary stat calculations
- `frontend/src/components/pages/BankStatementImportChart.tsx` — added `?? []` on `data.statistics` and `?? 0` on summary stat calculations
- `frontend/src/components/customer/tabs/StatisticsTab.tsx` — added `?? 0` fallbacks on `importCount`/`totalItemCount` usages

## Artifacts
- Brief, spec, arch-review, task-plan, impl, and review markdown are committed in this branch under `artifacts/feat-3333/`.
