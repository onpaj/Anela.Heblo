# Implementation: fix-batch-planning-error-handling-e2e-navigation

## What was implemented
Fixed `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts` so both tests reliably
navigate to and load `/manufacturing/batch-planning` (component `BatchPlanningCalculator`) instead of
silently landing on `/manufacturing/batch-calculator`. Applied the three edits described in the task
context, copied from the proven-working sibling `batch-planning-workflow.spec.ts`:

1. **Navigation selector (FR-1)** — replaced `page.locator('text=/Plánovač|Kalkulačka dávek/i').first()`
   with the role-based `page.getByRole('link', { name: /plánování dávek/i })` in both tests' navigation
   blocks. This matcher does not match "Kalkulačka dávek" (which routes to the wrong page), so it can no
   longer silently mis-navigate. The existing `isVisible` → click / `page.goto` fallback structure was
   kept as-is (fallback only fires when the link is genuinely absent).
2. **Heading assertion (FR-2)** — replaced the permissive
   `page.locator('h1, h2').filter({ hasText: /Plánovač|Planning|Dávek|Kalkulačka/i })` with the exact
   `page.locator('h1').filter({ hasText: /plánovač výrobních dávek/i })` in test 1, and **added** the
   same exact-heading assertion to test 2 (which previously had no page-loaded guard at all before
   touching the combobox).
3. **Load-wait strategy (FR-4)** — replaced every `page.waitForLoadState('networkidle')` in the file
   (in `beforeEach` and after each navigation/action) with `page.waitForLoadState('domcontentloaded')`,
   matching the workflow spec. All 7 occurrences in the file were changed.

Everything else was left untouched: the combobox interaction, the `TestCatalogItems.hedvabnyPan` /
`MAS001001M` fixture usage, the "missing test data" `throw` guards, the checkbox/quantity/`Přepočítat`
button flow in test 1, the `10`/no-error-toaster flow in test 2, and `navigateToApp(page)` for auth.
No other files were modified.

## Files created/modified
- `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts` — navigation selector,
  heading assertion (added to both tests), and wait-strategy changes as described above.

## Tests
- **Lint**: `npm run lint` (from `frontend/`) was run — it only lints the `src` directory
  (`eslint src --ext .ts,.tsx` per `package.json`), so this E2E spec file is outside its scope and the
  command's outcome is unaffected by this change (its 148 pre-existing errors/warnings are all in
  unrelated `src/**/__tests__` files, not touched here).
- Ran `node_modules/.bin/eslint` directly against the modified file (installed local ESLint 8.57.1,
  since `npx eslint` was resolving an incompatible v10 with no flat config). It reports 4
  `testing-library/prefer-screen-queries` errors, all false positives from the `react-app/jest` ESLint
  preset misfiring on Playwright's `page.getByRole(...)` calls. Confirmed this is a pre-existing,
  file-wide false-positive pattern by running the same direct lint against the untouched reference
  sibling `batch-planning-workflow.spec.ts`, which reports 28 similar/related pre-existing errors
  (including on its own `page.getByRole` navigation lines that this task explicitly told us to copy
  verbatim). No new class of lint issue was introduced.
- Ran a standalone `tsc --noEmit` type-check against the file: one pre-existing error at line 231
  (`hasClass` is not a valid Playwright locator filter option) — present before this change, on a line
  this task did not touch (visual-indicator check, explicitly marked non-critical/logged-only in the
  task context). No new type errors were introduced by the edits.
- Manually diffed the file against the task context's required changes line-by-line; confirmed no
  remaining `networkidle` occurrences (`grep -n networkidle` → no matches) and that both tests now have
  the exact `<h1>` heading guard.
- **Not performed**: a live Playwright run against staging (`./scripts/run-playwright-tests.sh
  manufacturing`) — no network access to `https://heblo.stg.anela.cz` in this sandboxed environment.
  This is deferred to the pipeline's verification step, per the task instructions.

## How to verify
1. `cd frontend && npm run lint` — confirm no new failures attributable to this file (it's out of that
   script's `src`-only scope).
2. `cd frontend && ./scripts/run-playwright-tests.sh manufacturing` (or the repo's standard E2E runner)
   against staging — confirm both tests in `batch-planning-error-handling.spec.ts` pass, and that
   `batch-planning-workflow.spec.ts` remains green (no regression).
3. Spot-check: after navigation in either test, `page.url()` ends with `/manufacturing/batch-planning`,
   the `<h1>` "Plánovač výrobních dávek" is visible, and selecting `MAS001001M` populates `tbody tr` rows.

## Notes
- Deviation from a literal reading of task step 5: `npm run lint` as configured in this repo does not
  include `frontend/test/**` at all, so it cannot "fail" or "pass" specifically for this file — it was
  still run per instruction, and the file was additionally lint-checked directly with the project's
  local ESLint binary for a more meaningful signal, per the instruction's fix-lint-issues intent.
- Kept the optional `page.goto('/manufacturing/batch-planning')` fallback structure exactly as it was
  (only fires when the link is not visible), matching task guidance that this is acceptable and that
  extracting a shared nav helper (FR-6) is explicitly deferred/out of scope.
- No dependency, config, or non-spec source files were changed. `npm install --legacy-peer-deps` was
  run locally in this worktree only to obtain a working ESLint/TypeScript toolchain for verification;
  no lockfile or `package.json` changes were made or committed.

## Status
DONE
