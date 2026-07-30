# Design: Relocate & rename BankStatementImportPage

## Scope note
This is a file relocation + rename with corrected import paths — no new UI, no behavior change, no data/schema surface. Per the plan, there is no UX/UI section (the rendered page is byte-for-byte identical) and no data schema section (nothing touches DTOs, API contracts, or persistence). What follows is the component/module design: exact before/after file contents for every touched line.

## Component design

### File move

| | Path |
|---|---|
| From | `frontend/src/components/pages/BankStatementImportChart.tsx` |
| To | `frontend/src/pages/customer/BankStatementImportPage.tsx` |

Mechanism: `git mv` (preserves history), followed by import-path edits inside the moved file. No changes to JSX, hooks, state, or styling.

### Import-path recalculation (verified against actual file, not assumed)

Both `components/pages/` and `pages/customer/` sit two directories below `frontend/src/` (`src/components/pages/` and `src/pages/customer/`), so most `../../`-rooted imports are unchanged. The one import rooted at `../` (one level, into a components/-relative sibling) changes because the moved file is no longer inside `components/`.

Confirmed by cross-checking `frontend/src/pages/customer/BankStatementsOverviewPage.tsx`, which already imports `../../telemetry/useScreenView` and `../../components/customer/tabs/...` from the same `pages/customer/` depth — validates the recalculation below.

```diff
- import { useBankStatementImportStatistics } from "../../api/hooks/useBankStatements";
+ import { useBankStatementImportStatistics } from "../../api/hooks/useBankStatements";
  (unchanged — same depth: src/pages/customer/ → src/api/hooks/ is still ../../)

- import { BankStatementImportChart } from '../charts/BankStatementImportChart';
+ import { BankStatementImportChart } from '../../components/charts/BankStatementImportChart';
  (changed — was components/pages/ → components/charts/ (../charts/...); now pages/customer/ → src/ → components/charts/)

- import { useScreenView } from '../../telemetry/useScreenView';
+ import { useScreenView } from '../../telemetry/useScreenView';
  (unchanged — same depth)
```

Only the `BankStatementImportChart` import (the real chart component) changes; the other two imports are copied as-is because the directory depth from `src/` is identical (2 levels) in both the old and new location.

### Export / identifier

No change: `const BankStatementImportPage: React.FC = () => { ... }` and `export default BankStatementImportPage;` stay exactly as they are today (`frontend/src/components/pages/BankStatementImportChart.tsx:17,267`). The file name now matches the export it already had.

### Registration point — `frontend/src/App.tsx`

Two lines change; the route path string does not.

```diff
- import BankStatementImportChart from "./components/pages/BankStatementImportChart";
+ import BankStatementImportPage from "./pages/customer/BankStatementImportPage";
```
(App.tsx:18 — module path updated to new location; local identifier renamed from `BankStatementImportChart` to `BankStatementImportPage` so the import site doesn't reintroduce the same page/chart name collision the finding is about.)

```diff
- <Route path="/finance/bank-statements" element={<BankStatementImportChart />} />
+ <Route path="/finance/bank-statements" element={<BankStatementImportPage />} />
```
(App.tsx:407 — component reference updated to match the renamed import; `path="/finance/bank-statements"` and the surrounding `Layout` route group / absence of `guard(...)` are untouched — that wrapper inconsistency is a separate, out-of-scope concern per the plan.)

Import ordering: place the new `import BankStatementImportPage from "./pages/customer/BankStatementImportPage";` where the old import line sat (line 18), or alongside the existing `import BankStatementsOverviewPage from "./pages/customer/BankStatementsOverviewPage";` (line 60) for grouping consistency — either is acceptable since `App.tsx` imports aren't strictly alphabetized by folder today; keep it adjacent to line 18's position to minimize diff noise.

### Untouched components (explicit boundary)

- `frontend/src/components/charts/BankStatementImportChart.tsx` — the real chart component. Not moved, not renamed, not edited. Only its consumer's import path changes (shown above).
- `frontend/src/components/customer/tabs/StatisticsTab.tsx` (lines 9, 248) — a second consumer of the real chart component. Unaffected; it doesn't reference the page being moved.
- `frontend/src/components/pages/` directory — remains in place with its ~30 other files. Not deleted.

## Data schemas
Not applicable. No DTOs, API contracts, request/response shapes, or event payloads are touched — `useBankStatementImportStatistics` and its underlying endpoint are consumed unchanged.

## Verification plan for the build step
1. `grep -rn "components/pages/BankStatementImportChart" frontend/src` → no hits after the move (only the new `pages/customer/BankStatementImportPage` path should appear).
2. `grep -n "BankStatementImportChart" frontend/src/App.tsx` → no hits (only `BankStatementImportPage` remains, per plan FR-4's acceptance criterion).
3. `npm run build` in `frontend/` → zero unresolved-module / TS errors.
4. `npm run lint` in `frontend/`.
5. Manual/dev-server check: navigate to `/finance/bank-statements`, confirm identical rendering (header, controls, summary tiles, chart, info panel) to pre-change behavior.
