## Review Result: CHANGES_REQUESTED

### Blocking (correctness)

- `frontend/src/components/charts/InvoiceImportChart.tsx:43-44` — `item.count` and `item.isBelowThreshold` are now `number | undefined` and `boolean | undefined` respectively (from the generated `DailyInvoiceCount` class, which marks all fields optional). They are assigned into `ChartDataPoint` slots typed as `number` and `boolean`. If the backend omits either field, `count` renders as `undefined` in Recharts (the line draws at zero/NaN and the threshold dot logic silently misfires). The old local `DailyInvoiceCount` interface required both fields — this diff weakened the contract without adding guards. Fix: add fallbacks, e.g. `count: item.count ?? 0` and `isBelowThreshold: item.isBelowThreshold ?? false`.

- `frontend/src/components/charts/BankStatementImportChart.tsx:50-51` — Same problem for `DailyBankStatementStatistics`. `item.importCount` and `item.totalItemCount` are `number | undefined` in the generated type (the old `BankStatementImportStatisticsDto` had them as required `number`). They are assigned directly into `ChartDataPoint.count` and `ChartDataPoint.itemCount` (both `number`). Additionally, `currentCount < minimumThreshold` on line 45 evaluates to `false` whenever `currentCount` is `undefined`, meaning days with missing counts are never flagged below threshold. Fix: `item.importCount ?? 0`, `item.totalItemCount ?? 0`.

### Advisory (cleanup)

- `frontend/src/api/hooks/useBankStatements.ts:51` — `await getAuthenticatedApiClient()` awaits a synchronous function (it returns `ApiClient`, not `Promise<ApiClient>`). The `await` is harmless at runtime but misleading; the sibling hook `useInvoiceImportStatistics` correctly dropped it. Consider removing the `await` here and making `queryFn` non-async for consistency.
