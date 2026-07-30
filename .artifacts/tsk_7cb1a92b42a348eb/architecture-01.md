# Architecture Review: Relocate & rename BankStatementImportPage

## Verdict
**Approved as designed.** This is a pure file move + relative-import fixup. No architectural invariant is at risk, and independent verification against the live repo confirms every path and scope claim made in `plan-01.md` and `design-01.md`.

## Verification performed (against actual repo state, not the artifacts' claims)

1. **`docs/architecture/filesystem.md` compliance** — confirmed the doc explicitly separates `frontend/src/components/` (reusable components) from `frontend/src/pages/` (page components), both with co-located `__tests__/`. It does **not** mandate feature-named subfolders under `pages/` (no `pages/{feature}/` rule is written down), so `pages/customer/` vs. a hypothetical new `pages/finance/` is a judgment call, not a documented invariant. The design's choice — reuse `pages/customer/` to match the only existing sibling bank page (`BankStatementsOverviewPage.tsx`) — is the right call: it avoids inventing a single-file folder and keeps the one precedent for "where bank pages live" intact. No objection.

2. **Import-path recalculation** — read the target file in full and cross-checked against the sibling `pages/customer/BankStatementsOverviewPage.tsx`, which already lives at the exact target depth and already contains `import { useScreenView } from '../../telemetry/useScreenView';`. This independently confirms the design's claim that `src/components/pages/` and `src/pages/customer/` are both two levels below `src/`, so:
   - `../../api/hooks/useBankStatements` — unchanged (verified `useBankStatementImportStatistics` is exported from `src/api/hooks/useBankStatements.ts`).
   - `../../telemetry/useScreenView` — unchanged (verified live in the sibling file).
   - `'../charts/BankStatementImportChart'` — the only path that must change, to `'../../components/charts/BankStatementImportChart'`. Confirmed `src/components/charts/BankStatementImportChart.tsx` exists and is the correct target.
   All three recalculations in `design-01.md` are correct.

3. **No path aliases to worry about** — `frontend/tsconfig.json` defines no `baseUrl`/`paths` mapping, so module resolution is purely relative-path-based. The plan/design's approach (recompute relative segments) is the only valid mechanism here; there's no alias shortcut being missed.

4. **Single registration point, confirmed by repo-wide grep** — `grep -rn "BankStatementImportPage|BankStatementImportChart"` across `frontend/src` returns exactly 4 files: the moved file itself, the real chart component (`components/charts/BankStatementImportChart.tsx`), its other consumer (`components/customer/tabs/StatisticsTab.tsx`, unaffected — imports the chart via its own unrelated relative path), and `App.tsx`. **`App.tsx` is the only place the page component is imported/registered** — the plan's FR-4 scope is complete, nothing missed.

5. **Route path is referenced elsewhere, but only as a string — no import risk.** Found one thing neither `plan-01.md` nor `design-01.md` called out: `src/components/dashboard/tiles/tileRegistry.tsx:83` has a dashboard tile with `targetUrl="/finance/bank-statements"`. This is a plain string URL used for client-side navigation, not a component import — since the plan correctly keeps the route path (`/finance/bank-statements`) unchanged, this file requires **no edit**. Flagging it here so the implementer doesn't need to re-discover it, and so "grep for the route path, not just the component name" is on record as the completeness check.

6. **`components/pages/` directory state** — confirmed 35 other files remain (plan said "~30", close enough; actual count re-verified at review time). Deletion is correctly excluded from scope.

7. **Chart-vs-page name collision, the actual root cause** — confirmed `components/charts/BankStatementImportChart.tsx` (real chart) and the page file both currently share the `BankStatementImportChart` name stem. The rename target (`BankStatementImportPage.tsx`) fully resolves the collision on both axes (directory and name) called out in the finding.

## Alignment with existing patterns
- Matches the established page/component split in `filesystem.md`.
- Matches the sibling `pages/customer/BankStatementsOverviewPage.tsx` precedent for both folder choice and import depth.
- No new abstractions, no DTO/contract changes, no backend touch — consistent with "surgical changes only" project guidance.

## Risks and mitigations
- **Risk**: transcription error in relative import paths during the manual edit. **Mitigation**: already covered by plan/design's verification step (`npm run build` must show zero unresolved-module errors) — sufficient, no additional tooling needed given this is a 3-import file.
- **Risk**: missing a second registration point. **Mitigation**: already ruled out by the repo-wide grep in this review (item 4 above); no hidden lazy-route config or nav-menu component-level reference exists beyond `App.tsx` and the string-only dashboard tile URL (item 5), which needs no change.
- **No risk to the real chart component** — it has exactly one other consumer (`StatisticsTab.tsx`) whose import path is untouched by this move.

## Implementation guidance (confirms plan/design; no changes required)
1. `git mv frontend/src/components/pages/BankStatementImportChart.tsx frontend/src/pages/customer/BankStatementImportPage.tsx`
2. Edit only the chart import inside the moved file: `'../charts/BankStatementImportChart'` → `'../../components/charts/BankStatementImportChart'`. Leave the other two imports untouched.
3. `App.tsx`: update line 18's import path/identifier and line 407's JSX usage, per `design-01.md`'s diff. Leave the route path string and the (intentionally guard-less) route wrapper untouched — that's a separate, out-of-scope inconsistency.
4. No other file needs to change. `tileRegistry.tsx` is unaffected (string URL only, verified above).
5. Validate with `npm run build` + `npm run lint` in `frontend/`, then a dev-server visual check of `/finance/bank-statements`.

## Prerequisites before implementation begins
None. The working tree is clean, the task has no dependency on other in-flight work, and every path/scope claim in the plan and design has now been independently verified against the current repo state.
