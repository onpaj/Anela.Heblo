# Implementation: guard-routes-in-app-tsx

## What was implemented
Wrapped the two unguarded routes identified in issue #4041 with the existing `guard(path, element)` helper, matching the pattern already used by every sibling route in `frontend/src/App.tsx`:
- `/finance/bank-statements` → `guard("/finance/bank-statements", <BankStatementImportPage />)`
- `/automation/invoice-import-statistics` → `guard("/automation/invoice-import-statistics", <InvoiceImportStatistics />)`

This depended on the prior `regenerate-access-matrix-artifacts` task, which already added the corresponding `ACCESS_ROUTES` entries for both paths, so `RequireMenuPath` resolves correctly at runtime instead of redirecting every user.

## Files created/modified
- `frontend/src/App.tsx` — two lines changed (one `-`/`+` pair each, in two separate hunks); no other route, import, or the `guard()` definition itself touched.

## Tests
No new tests in this task — test coverage for the `ACCESS_ROUTES` consistency check (every `guard()` call has a matching entry) was added in the earlier `regenerate-access-matrix-artifacts` task and already covers these two paths. The next task (`validate-and-run-tests`) runs the full validation pass.

## How to verify
```bash
grep -n 'finance/bank-statements"\|invoice-import-statistics"' frontend/src/App.tsx
git show HEAD -- frontend/src/App.tsx
```
Both routes should show `element={guard(...)}` rather than a bare element.

## Notes
`npx tsc --noEmit` ran clean (only pre-existing tsconfig deprecation warnings, unrelated to this change). No deviations from the task spec.

## PR Summary
Closed a permission-gate gap: two frontend routes (`/finance/bank-statements`, `/automation/invoice-import-statistics`) called backend endpoints requiring `Finance_MarginAnalysis`, but the routes themselves weren't guarded like their siblings, so an unauthorized user could navigate to a page that then 403'd on data fetch. Both routes are now wrapped in the standard `guard(path, element)` helper.

### Changes
- `frontend/src/App.tsx` — wrapped `/finance/bank-statements` and `/automation/invoice-import-statistics` routes in `guard(...)`

## Status
DONE
