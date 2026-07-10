# Implementation: remove-dead-severity-exports

## What was implemented
Removed the two dead exported helper functions `getSeverityColorClass` and `getSeverityDisplayText` (and their preceding comments) from `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`. These had zero consumers anywhere in `frontend/src` — `PurchaseStockAnalysis.tsx` implements its own inline severity styling and never imported either export.

## Files created/modified
- `frontend/src/api/hooks/usePurchaseStockAnalysis.ts` — deleted the `getSeverityColorClass` and `getSeverityDisplayText` functions plus their two preceding comments and the blank line separating them from the next comment (40 lines removed, no lines added). `usePurchaseStockAnalysisQuery`, `formatNumber`, `formatCurrency`, and all other exports in the file are untouched.

## Tests
There are no dedicated unit tests for this hook file. Verification was done via:
- `grep -n "getSeverityColorClass\|getSeverityDisplayText" frontend/src/api/hooks/usePurchaseStockAnalysis.ts` — zero matches (confirmed removed).
- `grep -n "export const usePurchaseStockAnalysisQuery\|export const formatNumber\|export const formatCurrency" frontend/src/api/hooks/usePurchaseStockAnalysis.ts` — three matches, confirming remaining exports intact.
- `cd frontend && grep -rn "getSeverityColorClass\|getSeverityDisplayText" src` — zero matches project-wide, confirming no consumers were missed.
- `git diff -- frontend/src/api/hooks/usePurchaseStockAnalysis.ts` — diff shows only 40 deleted lines (the two functions, their comments, and one blank line), no additions, no other changes.
- `cd frontend && npm run build` (with `CI=true`) — compiled successfully, no errors.
- `cd frontend && npm run lint` — 162 pre-existing problems reported, all in unrelated test files (testing-library rules in marketing/, financial-overview/, terminal/, contexts/, leaflet-generator/ test specs); zero issues in `usePurchaseStockAnalysis.ts` (confirmed via `npm run lint 2>&1 | grep -i usePurchaseStockAnalysis` returning no output). Nothing new was introduced by this change.

## How to verify
1. `cd frontend && npm install --legacy-peer-deps` (node_modules was not pre-installed in this environment; a plain `npm install`/`npm ci` fails due to a pre-existing `react-i18next` peer-dependency conflict against `typescript@4.9.5` — unrelated to this change).
2. `npm run build` — should compile successfully.
3. `npm run lint` — should show no errors for `usePurchaseStockAnalysis.ts`.
4. `git show f4520703979fd7e9cf42e76560e316bd33d4e422` — review the commit diff, should only show the 40 deleted lines in the target file.
5. `grep -rn "getSeverityColorClass\|getSeverityDisplayText" src` (from `frontend/`) — should return no matches.

## Notes
- `node_modules` was not present in the worktree; had to run `npm install --legacy-peer-deps` to get `react-scripts`/eslint available for build and lint. This is an existing, unrelated peer-dependency conflict in the repo's `package.json` (react-i18next 15.7.4 wants TypeScript 5, project pins TypeScript 4.9.5) — not something introduced or fixed by this task, flagging for awareness only.
- `git status` after the commit shows an unrelated pre-existing modification to `artifacts/feat-3582/state.json` in the working tree; this was not touched by this task and was left as-is (appears to be pipeline-managed state).

## PR Summary
Deleted two dead exported helper functions, `getSeverityColorClass` and `getSeverityDisplayText`, from `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`. Both were confirmed to have zero consumers anywhere in `frontend/src` via project-wide grep before and after the change. `PurchaseStockAnalysis.tsx` implements its own inline severity styling and never referenced either export; `getSeverityColorClass` additionally lacked dark-mode variants, which would have been a latent ADR-006 violation had it ever been wired up. All other exports in the file (`usePurchaseStockAnalysisQuery`, `formatNumber`, `formatCurrency`, and the re-exported generated types) are unaffected. Verified with a scoped `git diff` (40 deletions only), a project-wide grep for the removed identifiers (no matches), `npm run build` (success), and `npm run lint` (no new issues; pre-existing unrelated failures only in test files elsewhere in the codebase).

### Changes
- `frontend/src/api/hooks/usePurchaseStockAnalysis.ts` — removed dead `getSeverityColorClass` and `getSeverityDisplayText` exports and their comments (40 lines deleted, nothing added).

## Status
DONE
