# Plan: Relocate & rename BankStatementImportPage (currently `BankStatementImportChart.tsx`)

## Summary
`frontend/src/components/pages/BankStatementImportChart.tsx` exports a full page component (`BankStatementImportPage`) but is named after — and shadows — the actual reusable chart at `frontend/src/components/charts/BankStatementImportChart.tsx`. Per `docs/architecture/filesystem.md`, page components belong under `frontend/src/pages/`, not `frontend/src/components/pages/`. This is a pure rename + move: no logic, markup, or behavior changes.

## Context
Verified directly against the current repo state (this task has no prior pipeline steps — it's the first artifact for tsk_7cb1a92b42a348eb):
- `frontend/src/components/pages/BankStatementImportChart.tsx` exports default `BankStatementImportPage`, a 265-line component with data fetching (`useBankStatementImportStatistics`), view/date-type toggle controls, summary tiles, loading/error states, and an embedded `<BankStatementImportChart />` from `../charts/BankStatementImportChart`.
- It is registered in `frontend/src/App.tsx:18` (import) and `frontend/src/App.tsx:407` (route `/finance/bank-statements`, inside the desktop `Layout` route group — no `guard(...)` wrapper, unlike most sibling routes).
- The real chart component (`components/charts/BankStatementImportChart.tsx`) is unrelated and must not be touched.
- No test file references either `BankStatementImportChart` or `BankStatementImportPage` under `__tests__/` — no test imports need updating.
- `frontend/src/components/pages/` is **not** an empty legacy folder — it holds ~30 other page-like components (`Dashboard.tsx`, `CatalogList.tsx`, `FinancialOverview.tsx`, `ManufacturedInventoryPage.tsx`, etc.), all wired into `App.tsx` the same way. Only this one file is in scope; the directory will not become empty and must not be deleted.
- Reference pattern: `frontend/src/pages/customer/BankStatementsOverviewPage.tsx` — a different, already-correctly-placed page (tabbed Statistics/Import view for the bank-statement customer overview, routed at `/customer/bank-statements-overview`). It's a sibling in the same "Bank" feature area, giving precedent for `pages/customer/` as the landing spot even though this page's route lives under `/finance/*`.
- There is no `frontend/src/pages/finance/` folder today, so `pages/customer/` (matching the sibling bank page) is the more consistent choice than inventing a new top-level folder for one file.

## Functional requirements

**FR-1: Move and rename the file**
- Move `frontend/src/components/pages/BankStatementImportChart.tsx` → `frontend/src/pages/customer/BankStatementImportPage.tsx`.
- No changes to the component's internal logic, JSX, hooks, or styling.
- Acceptance: `git mv` diff shows only a rename (or rename + import-path fixups), no logic diff.

**FR-2: Fix internal relative imports after the move**
- The file currently imports:
  - `useBankStatementImportStatistics` from `"../../api/hooks/useBankStatements"`
  - `BankStatementImportChart` from `'../charts/BankStatementImportChart'`
  - `useScreenView` from `'../../telemetry/useScreenView'`
- New location is one directory deeper (`pages/customer/` vs `components/pages/`), so relative paths must be recalculated, not copy-pasted:
  - `../../api/hooks/useBankStatements` → `../../api/hooks/useBankStatements` (same depth: `components/pages/` → `src/`, `pages/customer/` → `src/` — both 2 levels up, verify exact segment count against actual final path)
  - `../charts/BankStatementImportChart` → `../../components/charts/BankStatementImportChart`
  - `../../telemetry/useScreenView` → `../../telemetry/useScreenView` (verify depth)
- Acceptance: `npm run build` (or `tsc --noEmit`) reports zero unresolved-module errors for this file.

**FR-3: Keep the export name as-is**
- The exported component is already named `BankStatementImportPage` — no rename of the identifier, only the file name changes to match it.
- Acceptance: `export default BankStatementImportPage;` unchanged.

**FR-4: Update the registration point in `App.tsx`**
- Update the import at `App.tsx:18` from `./components/pages/BankStatementImportChart` to `./pages/customer/BankStatementImportPage`.
- Import identifier can stay `BankStatementImportChart` renamed to `BankStatementImportPage` for clarity (recommended), or keep the existing local name — but since the finding calls out the misleading name as the core problem, the import alias should also be updated to `BankStatementImportPage` so `App.tsx` doesn't reintroduce the same confusion.
- The route itself (`path="/finance/bank-statements"`) is unchanged — this is not a URL/route change, only a source-file relocation.
- Acceptance: route `/finance/bank-statements` still renders the same component; `grep -n "BankStatementImportChart" frontend/src/App.tsx` returns no hits after the change (only `BankStatementImportPage` remains).

**FR-5: Do not touch the real chart component**
- `frontend/src/components/charts/BankStatementImportChart.tsx` and its usages (`components/pages/...` import, `components/customer/tabs/StatisticsTab.tsx:9,248`) are out of scope and must remain unchanged.
- Acceptance: `git diff` shows no changes under `components/charts/` or `components/customer/tabs/StatisticsTab.tsx`.

**FR-6: Directory cleanup — explicitly out of scope**
- Do NOT delete `frontend/src/components/pages/` — confirmed non-empty (30+ other files). The suggested-fix note "delete the now-empty directory if it becomes empty" does not apply here and should be dropped from the actual work.

## Non-functional requirements
- No behavior change: same route, same props, same rendered output — this is a structural/organizational fix only, verifiable by the page rendering identically before/after in the browser.
- No new dependencies, no new abstractions introduced.

## Data model
Not applicable — no entities, DTOs, or API contracts are touched.

## Interfaces
- **Route**: `/finance/bank-statements` (unchanged) in `App.tsx`, now pointing at `frontend/src/pages/customer/BankStatementImportPage.tsx`.
- **Component export**: `BankStatementImportPage` (default export), signature and props unchanged (no props — it's a route-level page).
- No API/backend interface changes.

## Dependencies and scope
- Depends on: nothing else in flight; this is an isolated frontend-only rename.
- In scope: the single file move, its 2–3 relative import path fixes, and the one import/reference update in `App.tsx`.
- Out of scope:
  - Splitting data-fetching from display/control logic within `BankStatementImportPage` (flagged in the finding as a secondary concern, not the primary violation — do not do this in the same change).
  - Any other file currently sitting in `components/pages/` (e.g., `Dashboard.tsx`, `FinancialOverview.tsx`) — this is a known broader inconsistency but not part of this task.
  - Adding the `guard(...)` wrapper to the `/finance/bank-statements` route to match sibling routes — unrelated to this finding, do not change.
  - Deleting `components/pages/` — it will not be empty after this change.

## Rough plan
1. `git mv frontend/src/components/pages/BankStatementImportChart.tsx frontend/src/pages/customer/BankStatementImportPage.tsx`.
2. Fix the three relative imports inside the moved file to account for the new directory depth (recompute each path against the actual final location, don't assume the counts in FR-2 without checking).
3. Update `frontend/src/App.tsx`: import path (line 18) and, if renaming the local identifier, the usage at line 407 (`<BankStatementImportChart />` → `<BankStatementImportPage />`) — keep the route path string itself unchanged.
4. Run `npm run build` and `npm run lint` in `frontend/` to confirm no unresolved imports/type errors.
5. Manually verify (or via existing E2E/dev server) that navigating to `/finance/bank-statements` still renders the page correctly.
6. `dotnet build`/`dotnet format` are not applicable (no backend touched) — skip.

## Open questions
- **Target folder**: the finding suggests `pages/customer/` "or whichever sub-folder bank screens live in," but the route lives under `/finance/*` while the only existing sibling bank page (`BankStatementsOverviewPage.tsx`) sits in `pages/customer/`, and there's no `pages/finance/` folder yet. Default: place it in `pages/customer/` alongside `BankStatementsOverviewPage.tsx` for consistency with the existing Bank-feature precedent, rather than creating a new single-file `pages/finance/` folder. Revisit if the architecture step disagrees.
- **Import alias in `App.tsx`**: whether to rename the imported identifier from `BankStatementImportChart` to `BankStatementImportPage` in `App.tsx`, or leave the local alias as-is and only change the module path. Default: rename the alias too, since leaving it as `BankStatementImportChart` in `App.tsx` reintroduces the exact naming confusion this task exists to fix.
- **`components/pages/` deletion**: the finding's suggested step 4 ("delete the now-empty directory") does not apply — confirmed 30+ other files remain there. Treating this as a non-action, not a blocker.
