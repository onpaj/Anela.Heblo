# Implementation: harden-stock-operations-error-state-e2e-test

## What was implemented

Fixed the `'should display error state on API failure'` E2E test, which previously passed
vacuously regardless of actual behavior — a masking bug identified alongside the root cause of
issue #3540. Also hardened the shared `waitForTableUpdate` helper to fail fast with a clear
diagnostic message when the page renders the error card instead of the generic 15s timeout.

## Files created/modified
- `frontend/test/e2e/stock-operations/navigation.spec.ts` — fixed the route-intercept glob from
  `'**/api/stock-up-operations**'` (kebab-case, never matched the real endpoint) to
  `'**/api/StockUpOperations**'` (matches the generated client's actual PascalCase URL); replaced
  the soft `if (isErrorVisible) {...} else { console.log(...) }` with a hard
  `await expect(errorMessage).toBeVisible({ timeout: 15000 })` assertion so the test genuinely
  fails if the error UI doesn't appear; removed the now-redundant fixed `waitForTimeout(3000)`.
- `frontend/test/e2e/helpers/stock-operations-test-helpers.ts` — `waitForTableUpdate` now also
  matches the error-card heading and throws a descriptive error (pointing at the likely missing
  permission) if it appears, instead of only timing out generically after 15s.

## Tests
This task modifies E2E test files themselves; no new unit tests. Verified:
- `grep -n "stock-up-operations\|isErrorVisible"` on the edited spec confirms no leftover
  kebab-case glob or the removed `isErrorVisible` variable (only the legitimate `/stock-up-operations`
  URL path string remains).
- `npx tsc --noEmit` against the edited `navigation.spec.ts` reports zero errors for that file.
- Confirmed via `grep -rl "waitForTableUpdate"` that all other `stock-operations` spec files using
  the helper are happy-path tests (badges, accept, filters, sorting, retry, panel, state/source
  filter) that never intentionally expect an error state at the point they call it — none are
  affected by the added error-throwing branch.

## How to verify
Cannot run the Playwright E2E suite from this sandbox (it targets staging and is excluded from
this pipeline's validation per project convention — nightly-only). Once task 1's fix and a manual
permission grant (task 3) are live on staging, `navigation.spec.ts`'s error-state test and the
other `stock-operations` specs relying on `waitForTableUpdate` should be re-run in the nightly
suite to confirm end-to-end.

## Notes
No deviations from the task-context plan. `npm run lint` intentionally skipped per the task's
Step 6 — it only covers `frontend/src`, not `frontend/test/e2e`.

## Status
DONE
