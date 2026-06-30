## Goal
Fix the code review findings below.

## Blocking findings to fix

1. **`frontend/src/components/pages/automation/InvoiceImportStatistics.tsx:176`** — `data.data` is `DailyInvoiceCount[] | undefined` (the generated type marks this field optional), but it is passed directly to `InvoiceImportChart`'s required `data: DailyInvoiceCount[]` prop without a `?? []` fallback. Fix: change `data={data.data}` to `data={data.data ?? []}`.

2. **`frontend/src/components/pages/automation/InvoiceImportStatistics.tsx:177`** — `data.minimumThreshold` is `number | undefined` (optional in generated type), but it is passed to `InvoiceImportChart`'s required `minimumThreshold: number` prop. Fix: change `minimumThreshold={data.minimumThreshold}` to `minimumThreshold={data.minimumThreshold ?? 0}`.

## Note on the date issue (NOT a bug — no fix needed)

The reviewer raised a potential timezone concern about converting ISO date strings to `Date` objects in `useBankStatements.ts`. Investigation shows the backend `GetBankStatementImportStatisticsRequest` uses `DateTime?` (not `DateOnly`) and the handler explicitly works in UTC (`DateTime.UtcNow.Date`). Sending UTC midnight timestamps is correct and consistent. No change needed there.

## Files to change
- `frontend/src/components/pages/automation/InvoiceImportStatistics.tsx` — add `?? []` and `?? 0` fallbacks

## Acceptance criteria
- `data.data ?? []` is used when passing to `InvoiceImportChart`
- `data.minimumThreshold ?? 0` is used when passing `minimumThreshold`
- TypeScript compiles without errors
