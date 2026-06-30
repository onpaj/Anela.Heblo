## Review Result: CHANGES_REQUESTED

### Blocking (correctness)

- `frontend/src/components/pages/automation/InvoiceImportStatistics.tsx:176` — `data.data` is now `DailyInvoiceCount[] | undefined` (the generated `GetInvoiceImportStatisticsResponse.data` is optional), but it is passed directly to `InvoiceImportChart`'s required `data: DailyInvoiceCount[]` prop without a `?? []` fallback. The diff added the equivalent `?? []` for `data.statistics` in the bank statements page but omitted it here. If the backend omits the field, the chart receives `undefined` and crashes at `.map()`. Fix: `data={data.data ?? []}`.

- `frontend/src/components/pages/automation/InvoiceImportStatistics.tsx:177` — `data.minimumThreshold` is now `number | undefined` (optional in the generated type; was required in the old hand-written type), but it is passed directly to `InvoiceImportChart`'s `minimumThreshold: number` prop (required). If `undefined`, the reference line renders at `y={undefined}` and all `isBelowThreshold` comparisons silently evaluate to `false`. The diff does not add a fallback. Fix: `minimumThreshold={data.minimumThreshold ?? 0}` (or whatever the appropriate sentinel value is).

- `frontend/src/api/hooks/useBankStatements.ts:61` — `startDate` and `endDate` string values (expected to be ISO date strings like `'2026-05-01'`) are converted to `Date` objects via `new Date(request.startDate)`, which the generated client then serialises back via `.toISOString()` producing a full UTC timestamp (`'2026-05-01T00:00:00.000Z'`). The old code sent the raw date string. If the C# backend parameter is typed as `DateOnly` (or if the controller strips the time component), this is harmless; but if the parameter is typed as `DateTime` with any timezone-aware binding, the UTC midnight value may be interpreted as the previous local day in a UTC+N deployment (e.g., the Azure Web App in CET). Confirm the backend parameter type for `StartDate`/`EndDate` on `analytics_GetBankStatementImportStatistics` and, if it is `DateOnly`, consider passing the original string directly or truncating to date-only before conversion.

### Advisory (cleanup)

- `frontend/src/api/hooks/useInvoiceImportStatistics.ts:4` — `UseInvoiceImportStatisticsParams.dateType` is typed as `'InvoiceDate' | 'LastSyncTime'` (a manually maintained string union), which duplicates the generated `ImportDateType` enum values. Now that `ImportDateType` is imported and re-exported from this file, `dateType` could be typed as `ImportDateType | undefined` directly, eliminating the parallel declaration.
