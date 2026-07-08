# Implementation: fix-navigate-to-catalog-fallback

## What was implemented
Restructured `navigateToCatalog` in the Playwright E2E auth helper so the direct-navigation
fallback (`page.goto(.../catalog)`) is reached unconditionally whenever the UI-navigation
`try` block did not already confirm landing on `/catalog` — not only when an exception was
thrown. Previously, an `isVisible({ timeout: 2000 })` resolving to `false` (a timeout-miss,
the actual staging failure mode from the incident) would fall through the `try` block silently
without ever reaching the fallback `goto`, leaving the page on whatever it was on before. Also
raised the `isVisible` timeouts for "Produkty" and "Katalog" from 2000ms to 5000ms to match
sibling helpers (`navigateToTransportBoxes`, `navigateToTransportBoxReceive`), added an early
`return` on confirmed UI-navigation success, and added a final self-check that throws a
descriptive error (referencing both attempted paths and the final URL) if the URL still
doesn't contain `/catalog` after both the UI path and the fallback have been attempted.

## Files created/modified
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — replaced the body of `navigateToCatalog`
  (previously lines 234-260) with the restructured version: `try` block now only performs UI
  navigation and returns early on confirmed success; `catch` only logs and falls through; an
  unconditional fallback `goto` sits after the `try/catch`; a final `if (!page.url().includes('/catalog'))`
  throws a descriptive `Error`. No other function in the file was touched.

## Tests
- **Manual read-through against all four failure/success paths** (per task-context validation
  step 1) — traced by hand and confirmed:
  - *UI success*: both `isVisible` checks resolve `true`, both clicks succeed, `page.url()`
    contains `/catalog` after the "Katalog" click → early `return`; fallback never runs.
  - *Timeout miss* (the actual staging failure mode): `produktySelector.isVisible({ timeout: 5000 })`
    resolves to `false` (no throw) → both `if` bodies skipped, `else` branches log → `try`
    completes normally, `catch` does not run → falls through to the unconditional fallback →
    `page.goto` succeeds → self-check passes → function returns normally.
  - *Click lands off-`/catalog`* (e.g. `RequireMenuPath` redirect): "Katalog" click resolves,
    `page.url()` doesn't contain `/catalog` → no early return → falls to fallback `goto` → if
    fallback also fails to land on `/catalog`, the final `if` throws the descriptive error.
  - *Thrown exception* (e.g. strict-mode locator violation): caught, logged via
    `console.log('❌ UI navigation failed:', e.message)`, falls through to the same
    unconditional fallback as the timeout-miss case.
  All four paths confirmed: none can return without `page.url().includes('/catalog')` being
  true; the only way out without that guarantee is the explicit `throw`.
- **Type-check**: The root `frontend/tsconfig.json` only includes `"src"`, so `test/e2e` is
  never covered by `npx tsc --noEmit` as configured for this project — that command passes
  (module-resolution deprecation warnings only, pre-existing and unrelated to `test/e2e`).
  For a scoped check, I stood up a temporary tsconfig (same compiler options, `include` pointed
  at `test/e2e/**/*.ts`) and ran `npx tsc --noEmit --project <temp-config>` after `npm install
  --legacy-peer-deps` (node_modules was not present in this sandbox and had to be installed;
  a plain `npm install` fails on a pre-existing `react-i18next`/`typescript` peer-dependency
  conflict unrelated to this change). Result: no new error categories from this change — the
  only line flagged in the diff (`e.message` inside `catch (e) {}` at the new line 270) has
  `TS18046: 'e' is of type 'unknown'` under strict mode, but this is the pre-existing pattern
  used verbatim in every sibling function in this same file (`navigateToTransportBoxes`,
  `navigateToStockOperations`, `navigateToTransportBoxReceive`, `navigateToInvoiceClassification`
  — all untouched, all already using `catch (e) { ... e.message }`), and is required by the
  task context's exact-match spec. The scoped check also surfaces ~40 pre-existing errors in
  unrelated spec files (`batch-planning-workflow.spec.ts`, `invoice-classification-history-actions.spec.ts`,
  etc.) confirming this is baseline noise, not something introduced here.
- **Lint**: `npm run lint` (the project script) only targets `src` (`eslint src --ext .ts,.tsx`)
  and never scans `test/e2e`, so it is structurally unaffected by this change — confirmed it
  runs and shows the same 162 pre-existing problems, all under `src`, none related to this file.
  As a targeted due-diligence check, also ran
  `npx eslint test/e2e/helpers/e2e-auth-helper.ts --no-eslintrc -c .eslintrc.json --ext .ts`
  directly against the modified file: exit code 0, no output — no violations.
- **Staging E2E** (`./scripts/run-playwright-tests.sh catalog` against `https://heblo.stg.anela.cz`):
  not run — no access to the staging environment from this sandbox. Pending/out of scope for
  this step; flagged below.

## How to verify
1. Read `frontend/test/e2e/helpers/e2e-auth-helper.ts` lines 234-290 and trace the four paths
   described above.
2. `cd frontend && npm install --legacy-peer-deps` (if `node_modules` isn't already present).
3. `cd frontend && npx tsc --noEmit` — passes (test/e2e not in scope of this config; pre-existing
   `node_modules/react-i18next` d.ts errors are unrelated/baseline).
4. `cd frontend && npm run lint` — passes for `src`; run
   `npx eslint test/e2e/helpers/e2e-auth-helper.ts --no-eslintrc -c .eslintrc.json --ext .ts`
   for a targeted check of the modified file (expect exit code 0).
5. Against staging: `./scripts/run-playwright-tests.sh catalog` and confirm all 9 catalog spec
   files' setup assertions pass, then `./scripts/run-playwright-tests.sh transport` and
   `./scripts/run-playwright-tests.sh stock-operations` to confirm no regression in sibling
   helpers.

## Notes
- Staging E2E verification (task-context validation step 4 — `./scripts/run-playwright-tests.sh catalog`
  against `https://heblo.stg.anela.cz`, all 9 catalog spec files / 84 tests, plus targeted
  transport/stock-operations regression runs) could **not** be run from this sandbox (no network
  access to staging) and is pending.
- The FR-2 out-of-band note (task-context validation step 5 — confirming the E2E service
  principal holds `products.catalog.read` per `frontend/src/auth/accessMatrix.generated.ts`,
  and whether the permissions fetch was observed to be abnormally slow in `?e2e=true` mode
  during the staging run) also could not be checked from this sandbox and needs a human/CI run
  against staging before the underlying issue is closed.
- `node_modules` was not present in this worktree; I ran `npm install --legacy-peer-deps` to get
  a working local toolchain for validation. A plain `npm install` (no flag) fails on a
  pre-existing `react-i18next@15.7.4` (wants `typescript@^5`) vs. `typescript@^4.9.5` (root
  `package.json`) peer-dependency conflict — unrelated to this change, not modified as part of
  this fix, and not committed (node_modules is gitignored).
- No deviations from the task-context's exact before/after code were made; the replacement was
  applied verbatim including console.log wording/emoji and the new throw message.

## PR Summary
Fixes the E2E `navigateToCatalog` helper so its direct-navigation fallback is unconditionally
reached whenever UI navigation via the sidebar ("Produkty" > "Katalog") doesn't confirm landing
on `/catalog` — previously the fallback only ran inside a `catch` block, so an `isVisible`
timeout-miss (no exception, just a `false` resolution) silently skipped both the UI click *and*
the fallback, leaving tests on the wrong page. This was the actual failure mode observed in the
staging incident. The fix also raises the "Produkty"/"Katalog" `isVisible` timeouts from 2000ms
to 5000ms to match sibling navigation helpers, adds an early return once UI navigation is
confirmed via URL check (guarding against a silent `RequireMenuPath` redirect-on-insufficient-permission
false positive), and adds a final self-check that throws a descriptive error — naming both
attempted paths and the final URL — if `/catalog` still wasn't reached after both attempts, so
failures are diagnosable at the helper call site rather than surfacing as generic downstream
assertion failures across the 9 catalog spec files that call it.

### Changes
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — restructured `navigateToCatalog`: unconditional
  fallback after `try/catch`, 2000ms→5000ms `isVisible` timeouts, early return on confirmed
  UI-navigation success, and a new throw on failure to reach `/catalog` via either path.

## Status
DONE_WITH_CONCERNS
