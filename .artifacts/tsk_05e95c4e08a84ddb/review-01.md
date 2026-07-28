# Review: BankStatementImportChart theme-aware colors

## Verdict: done

## Checks performed

**Conformance to finding/spec** — All 9 hardcoded hex literals identified in the finding are replaced with `colors.*` lookups: `CartesianGrid.stroke`, `XAxis.stroke`, `YAxis.stroke`, `ReferenceArea.fill`, `ReferenceLine.stroke` + label `style.fill`, `Line.stroke` + `activeDot.fill`, and `CustomDot`'s `fill`/`stroke`. Verified against the live file (`frontend/src/components/charts/BankStatementImportChart.tsx`), not just the commit message.

**Adherence to architecture-01.md** — The required change (reuse `GRAPHITE.border` / `GRAPHITE.muted` / `GRAPHITE.surface` from `reactSelectDarkStyles.ts` instead of approximated hex values) is implemented exactly as specified: imports added, `colors` object matches the architecture's prescribed mapping token-for-token, semantic-hue tokens (`threshold`, `weekendFill`) and `line` left as designed.

**Scope discipline** — Only `BankStatementImportChart.tsx` was edited; `reactSelectDarkStyles.ts` is imported from, not modified, consistent with the finding's "no changes outside this one component file" constraint (which governs edits, not imports of existing exported constants).

**Correctness** — `colors` is derived once per render from `useTheme()` inside the component body, before `CustomDot`'s closure is defined, so `CustomDot` correctly captures the current render's `colors.threshold`/`colors.dotStroke` values (no stale-closure risk, since `CustomDot` is redefined every render anyway).

**Test coverage** — New test file exercises light and dark themes, asserting every affected SVG attribute resolves to the correct color in both modes via a mocked `ThemeContext` and a `ResponsiveContainer` mock (needed because jsdom reports zero width/height). No prior test existed for this component, so this is net-new coverage, not a modification.

## Verification actually run this turn (not just re-reading claims)

- `CI=true npx react-scripts test --watchAll=false --testPathPattern="charts"` → 3 suites / 13 tests pass, including both new light/dark tests.
- `CI=true npm run build` → compiles successfully, no type errors surfaced by CRA's build-time type checker.
- `npx tsc --noEmit` (run directly, outside CRA) reports pre-existing `react-i18next` `.d.ts` parse errors unrelated to this change — confirmed by checking out the pre-task commit (`b2ed892a`) and re-running: identical errors appear with none of this task's code present. Not a regression; environment/TS-version mismatch on a dependency's type declarations, immaterial since CRA's own build (which does typecheck) passes clean. Working tree was restored to the task's HEAD (`a3523cdd`) afterward — `git status` confirms clean.
- `npx eslint` on the component file alone: 0 problems. On the new test file: 28 `testing-library/no-container` / `no-node-access` errors — confirmed these stem from direct SVG attribute assertions (`container.querySelector(...).getAttribute('stroke')`), for which Testing Library has no role-based query equivalent. Cross-checked against `FinancialDataCards.test.tsx` (an already-merged, pre-existing test file), which has the identical violation pattern — so this is consistent with established (if lint-imperfect) precedent, not a new regression.
- Confirmed `GRAPHITE` is exported from `reactSelectDarkStyles.ts` and its `border`/`muted`/`surface` values match what architecture-01.md cited.

No functional requirement, architecture directive, or correctness issue found. Nothing to send back.
