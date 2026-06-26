## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/api/hooks/useBankStatements.ts:68,147,191` — `useBankStatementsList`, `useBankStatementImport`, and `useBankStatementAccounts` still use `await getAuthenticatedApiClient()`, but `getAuthenticatedApiClient` is synchronous (returns `ApiClient` directly, not a `Promise`). `await` on a non-Promise returns the value unchanged, so this is harmless at runtime, but the pattern is misleading and inconsistent with the corrected `useBankStatementImportStatistics`. These three functions were pre-existing (not introduced by this branch) and are outside the scope of this diff, so they do not block the PR.

---

### Fix verification

All four prior-round blocking issues confirmed applied in the worktree:

1. `InvoiceImportStatistics.tsx` — `data.data ?? []` and `data.minimumThreshold ?? 0` present.
2. `useBankStatements.ts` — `queryFn` is no longer `async`; `getAuthenticatedApiClient()` is called synchronously.
3. `InvoiceImportChart.tsx` — `count: item.count ?? 0` and `isBelowThreshold: item.isBelowThreshold ?? false` present.
4. `BankStatementImportChart.tsx` — `currentCount ?? 0`, `count: item.importCount ?? 0`, `itemCount: item.totalItemCount ?? 0` present; `data.statistics ?? []` present in `BankStatementImportChart.tsx` page component.

The generated client types (`DailyBankStatementStatistics`, `DailyInvoiceCount`, etc.) declare `date?: Date` — the `!` non-null assertions in both chart components are safe because the response processing in the generated client only populates `date` as `new Date(...)` or `<any>undefined`, and the parent components guard on `data ?` before rendering the charts. The `item.date!` assertions will not blow up at runtime for any item actually returned by the API.
