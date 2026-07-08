# Code Review: harden-stock-operations-error-state-e2e-test

## Summary
The commit exactly matches the task spec's prescribed diff: the route-intercept glob was
corrected from kebab-case (`**/api/stock-up-operations**`, never matched) to the generated
client's real PascalCase URL (`**/api/StockUpOperations**`, verified against
`frontend/src/api/generated/api-client.ts:12051`), the soft `isErrorVisible`/`if/else` assertion
was replaced with a hard `expect(errorMessage).toBeVisible(...)`, and `waitForTableUpdate` now
fails fast with a diagnostic error when the error card appears instead of timing out generically.
All claims in the implementation summary are verifiably accurate.

## Review Result: PASS

### task: harden-stock-operations-error-state-e2e-test
**Status:** PASS

## Docs to Update
None.

## Overall Notes
- Verified `grep -n "stock-up-operations\|isErrorVisible" navigation.spec.ts` returns only the
  legitimate `/stock-up-operations` URL path occurrences (lines 13 and 99) — no leftover glob or
  removed variable.
- Verified the error heading text `Chyba při načítání operací` and retry button text
  `Zkusit znovu` in the assertions match the actual rendered markup in
  `frontend/src/pages/StockOperationsPage.tsx:383` and `:391`.
- Verified `waitForTableUpdate`'s new error-heading selector (`h3` filtered by
  `Chyba při načítání operací`) matches the same DOM element, and confirmed via
  `grep -rln "waitForTableUpdate" frontend/test/e2e/stock-operations/` that all eight other spec
  files calling the helper are happy-path tests that don't intentionally expect the error state at
  that call site, so the new throw is safe.
- Ran `npx tsc --noEmit -p tsconfig.json --skipLibCheck test/e2e/stock-operations/navigation.spec.ts`
  — zero errors reported for the edited file, confirming the impl summary's type-check claim.
- Removal of the fixed `waitForTimeout(3000)` is correct/beneficial: the following hard
  `expect(...).toBeVisible({ timeout: 15000 })` already waits, so the extra sleep was redundant.
- Per the task's Step 6, `npm run lint` does not cover `frontend/test/e2e`, so its omission is
  expected and not a gap.
- Could not run the Playwright suite against staging (out of scope per task instructions); review
  is based on static diff/code inspection only, as directed.
