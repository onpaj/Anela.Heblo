# Code Review Fixes – Round 2

**Commit:** fix(analytics): add ?? fallbacks for optional generated type fields in chart components (#3333)

## Changes applied

### Fix 1: `InvoiceImportChart.tsx`

In the `.map((item) => ({...}))` block:
- `count: item.count` → `count: item.count ?? 0`
- `isBelowThreshold: item.isBelowThreshold` → `isBelowThreshold: item.isBelowThreshold ?? false`

Rationale: `DailyInvoiceCount` has both fields as optional (`number | undefined`, `boolean | undefined`). `ChartDataPoint` requires `number` and `boolean`. The `?? 0` / `?? false` fallbacks satisfy the type contract and prevent `undefined` from being rendered into the chart.

### Fix 2: `BankStatementImportChart.tsx`

In the `.map((item) => {...})` block:
- `const currentCount = viewType === ... ? item.importCount : item.totalItemCount` → added `?? 0` so the threshold comparison `currentCount < minimumThreshold` never operates on `undefined`
- `count: item.importCount` → `count: item.importCount ?? 0`
- `itemCount: item.totalItemCount` → `itemCount: item.totalItemCount ?? 0`

Rationale: `DailyBankStatementStatistics` has `importCount` and `totalItemCount` as optional. The `currentCount` intermediate variable is used in a numeric comparison, so the `?? 0` there is the critical guard. The return-object fallbacks ensure chart rendering receives well-typed `number` values.

### Fix 3: `useBankStatements.ts`

In `useBankStatementImportStatistics`:
- Removed `async` from `queryFn` signature
- Removed `await` from `getAuthenticatedApiClient()` call

Rationale: `getAuthenticatedApiClient()` returns `Promise<ApiClient>` and the call chain (`apiClient.analytics_GetBankStatementImportStatistics(...)`) itself returns the promise. Wrapping the outer function in `async` added a redundant layer; removing it keeps the pattern consistent with other hooks and avoids a double-wrapping of the returned promise.
