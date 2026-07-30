# Review: Relocate & rename BankStatementImportPage

## Verdict: done

## What I checked

- Read `plan-01.md`, `design-01.md`, `development-01.md` and the actual commit diff (`41d8c466`).
- Inspected `frontend/src/App.tsx` and `frontend/src/pages/customer/BankStatementImportPage.tsx` directly.
- Ran `grep -rln "BankStatementImportChart\|BankStatementImportPage" src` — exactly 4 hits: `App.tsx`, the real chart component (`components/charts/BankStatementImportChart.tsx`), its other consumer `StatisticsTab.tsx`, and the moved page. No stale references to the old path.
- Confirmed `frontend/src/components/pages/BankStatementImportChart.tsx` no longer exists, and the new file exists at `frontend/src/pages/customer/BankStatementImportPage.tsx`.
- Compared the moved file's import depth against sibling `pages/customer/BankStatementsOverviewPage.tsx` — the pattern (`../../api/...`, `../../components/...`, `../../telemetry/...`) matches exactly.
- Confirmed `tileRegistry.tsx` only references the route path string `/finance/bank-statements`, not the component — correctly left untouched.
- Ran `npm run build` from a clean checkout of this branch: compiled successfully, no TypeScript/module-resolution errors.
- Ran `npm run lint`: 160 problems (147 errors, 13 warnings) — identical to the pre-existing count claimed in the dev writeup, and confirmed none of the lint errors are in `App.tsx` or the moved file.
- Confirmed `components/pages/` still contains 34 other files (not deleted, correctly out of scope since it's non-empty per the original finding's conditional instruction).

## Assessment

This is a pure move + rename + two import-path fixes, exactly matching the finding's suggested fix and the plan/design scope:
1. File moved from `components/pages/` to `pages/customer/`, preserving the correct (already-matching) export name `BankStatementImportPage`. ✓
2. Internal chart import path corrected for the new directory depth (`../charts/...` → `../../components/charts/...`). ✓
3. `App.tsx` import and JSX usage updated to the new path/name; route path string unchanged (correctly out of scope). ✓
4. `components/pages/` directory left in place since it still holds 34 other files — correct per the finding's conditional ("delete... if it becomes empty"). ✓
5. No logic/behavior changes — verified via diff, matches design spec exactly.

No functional requirement is unmet, no architecture conflict, and build/lint verification is real and reproducible (not just claimed — I independently reran both). This resolves the arch-review finding cleanly with a minimal, surgical diff.

```json
{"outcome": "done", "summary": "Verified the move/rename of BankStatementImportChart.tsx to pages/customer/BankStatementImportPage.tsx against the original finding, plan, and design. Confirmed no stale references, correct import-path fixes, App.tsx registration update, and untouched components/pages/ and components/charts/BankStatementImportChart.tsx. Independently reran npm run build (clean) and npm run lint (160 problems, unchanged from baseline, none in touched files). No issues found."}
```
