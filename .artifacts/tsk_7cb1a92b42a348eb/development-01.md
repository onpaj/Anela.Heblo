# Development: Relocate & rename BankStatementImportPage

## Summary
Implemented the plan/design/architecture exactly as specified — a pure file move + rename with corrected relative import paths and one updated registration point. No logic, markup, or behavior changes.

## Changes

1. **Moved and renamed** (via `git mv`, preserving history):
   `frontend/src/components/pages/BankStatementImportChart.tsx` → `frontend/src/pages/customer/BankStatementImportPage.tsx`

2. **`frontend/src/pages/customer/BankStatementImportPage.tsx`** — one import line updated to account for the new directory depth:
   ```diff
   - import { BankStatementImportChart } from '../charts/BankStatementImportChart';
   + import { BankStatementImportChart } from '../../components/charts/BankStatementImportChart';
   ```
   The other two imports (`../../api/hooks/useBankStatements`, `../../telemetry/useScreenView`) were left unchanged — same depth from `src/` in both old and new locations, as verified in `design-01.md`. `export default BankStatementImportPage;` is untouched.

3. **`frontend/src/App.tsx`** — registration point updated (2 lines):
   ```diff
   - import BankStatementImportChart from "./components/pages/BankStatementImportChart";
   + import BankStatementImportPage from "./pages/customer/BankStatementImportPage";
   ...
   - <Route path="/finance/bank-statements" element={<BankStatementImportChart />} />
   + <Route path="/finance/bank-statements" element={<BankStatementImportPage />} />
   ```
   Route path string (`/finance/bank-statements`) and the route's guard-less wrapper are unchanged (out of scope, per plan).

## Untouched, as required
- `frontend/src/components/charts/BankStatementImportChart.tsx` (the real chart component) — not moved, not renamed, not edited.
- `frontend/src/components/customer/tabs/StatisticsTab.tsx` — the chart's other consumer, unaffected.
- `frontend/src/components/pages/` — still contains 34 other files; not deleted.

## Tests
No test files existed for this component before the change (`components/pages/__tests__/` has no `BankStatementImportChart`/`BankStatementImportPage` test, confirmed by search), so none needed updating or moving. No new tests were added — this is a pure rename/move with no new behavior to cover, consistent with the plan's scope (splitting data-fetching/display logic, which would warrant new unit tests, is explicitly out of scope for this task).

## Verification performed

1. **Repo-wide reference check** — `grep -rln "BankStatementImportChart\|BankStatementImportPage" src` returns exactly 4 files: `App.tsx`, the real chart component, `StatisticsTab.tsx`, and the moved page. No stale references to the old path remain.
2. **`npm install --legacy-peer-deps`** — dependencies were not pre-installed in this environment; installed to enable build/lint verification (pre-existing `react-i18next`/`typescript` peer-dep conflict in `package.json`, unrelated to this change — flagging in case it's not already known).
3. **`npm run build`** — compiled successfully, zero TypeScript/unresolved-module errors.
4. **`npm run lint`** — 160 problems (147 errors, 13 warnings), identical count/content before and after the change (verified via `git stash` / `git stash pop` A-B comparison). All pre-existing issues live in unrelated test files (photobank, financial-overview, terminal, ThemeContext, etc.) — none in `App.tsx` or the moved file.
5. **`git diff --cached`** — confirms the diff is exactly what `design-01.md` specified: a clean rename (98% similarity) with one import-line change inside the moved file, plus a 2-line diff in `App.tsx`. No unintended changes.

## How to verify
```
cd frontend
npm run build   # expect: "Compiled successfully."
npm run lint    # expect: 160 problems (147 errors, 13 warnings), none in App.tsx or pages/customer/BankStatementImportPage.tsx
```
Manual/dev-server check: navigate to `/finance/bank-statements` and confirm the page renders identically to before (header, date/view controls, summary tiles, chart, info panel).

```json
{"outcome": "done", "summary": "Moved and renamed components/pages/BankStatementImportChart.tsx to pages/customer/BankStatementImportPage.tsx, fixed its chart import path, and updated App.tsx's import/route registration. Verified via repo-wide grep (no stale references), npm run build (compiles clean), and npm run lint (identical pre-existing error count before/after, none in touched files)."}
```
