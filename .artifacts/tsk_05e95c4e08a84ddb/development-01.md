# Development: BankStatementImportChart theme-aware colors

## Summary

Implemented the architecture-approved fix (`architecture-01.md`) for ADR-006 dark-mode compliance in `BankStatementImportChart.tsx`. The chart now reads `useTheme()` and derives a `colors` object used for every Recharts SVG prop that was previously hardcoded to a light-mode hex value. Three of the six tokens reuse the existing `GRAPHITE` palette constants from `reactSelectDarkStyles.ts` (as required by the architecture review) instead of inventing approximate values; the two semantic-hue tokens (`threshold`, `weekendFill`) and the theme-invariant `line` color are unchanged from the design.

## Files changed

- **`frontend/src/components/charts/BankStatementImportChart.tsx`** (only file edited, per the finding's scope constraint):
  - Added imports: `useTheme` from `../../contexts/ThemeContext`, `GRAPHITE` from `../common/reactSelectDarkStyles`.
  - Added, at the top of the component body: `const { theme } = useTheme(); const isDark = theme === 'dark';` and a `colors` object:
    ```ts
    const colors = {
      grid: isDark ? GRAPHITE.border : '#f0f0f0',
      axis: isDark ? GRAPHITE.muted : '#6b7280',
      weekendFill: isDark ? '#0ea5e9' : '#e0f2fe',
      threshold: isDark ? '#f87171' : '#dc2626',
      line: '#3b82f6',
      dotStroke: isDark ? GRAPHITE.surface : '#fff',
    };
    ```
  - Replaced all 9 hardcoded hex literals with `colors.*` references:
    - `CustomDot` circle: `fill`/`stroke` → `colors.threshold` / `colors.dotStroke`
    - `CartesianGrid.stroke`, `XAxis.stroke`, `YAxis.stroke` → `colors.grid` / `colors.axis` / `colors.axis`
    - `ReferenceArea.fill` → `colors.weekendFill`
    - `ReferenceLine.stroke` + label `style.fill` → `colors.threshold`
    - `Line.stroke` + `activeDot.fill` → `colors.line`
  - No prop/interface/API changes; legend markup (Tailwind classes) and `CustomTooltip` untouched, matching FR-4 of the plan.

- **`frontend/src/components/charts/__tests__/BankStatementImportChart.test.tsx`** (new): smoke test that renders the component under mocked light/dark themes and asserts every affected SVG prop resolves to the correct light or dark color. No test previously existed for this component (confirmed absent by the plan/architecture review), so this is net-new coverage rather than a modification.
  - Mocks `recharts`'s `ResponsiveContainer` to bypass jsdom's zero-width/height container (recharts otherwise renders no children at all in jsdom — verified directly), replacing it with a fixed 800×400 wrapper so the real chart primitives mount.
  - Mocks `../../../contexts/ThemeContext` per-file (overriding the global light-only mock in `src/setupTests.ts`) so both themes can be exercised via `useTheme.mockReturnValue(...)`.
  - Two tests: `light` theme asserts grid/axis/reference-area/reference-line/line/dot colors equal the original light-mode literals (regression guard — FR-3); `dark` theme asserts they equal `GRAPHITE.border`, `GRAPHITE.muted`, `'#0ea5e9'`, `'#f87171'`, unchanged `'#3b82f6'`, and `GRAPHITE.surface`/`'#f87171'` for the dot.

## Verification performed

- `npm run build` — compiled successfully (no errors).
- `npx tsc --noEmit` — no type errors.
- `CI=true npx react-scripts test --watchAll=false --testPathPattern="charts"` — all 3 suites / 13 tests pass, including the 2 new tests.
- `npm run lint` — this repo's lint has a pre-existing baseline of 160 problems (147 errors) on `main` before this change (confirmed via `git stash -u` + re-run), entirely from the `testing-library/no-node-access` and `testing-library/no-container` rules being violated by many already-committed test files (e.g. `FinancialDataCards.test.tsx`, `ScanInput.test.tsx`, `LocationSelectionModal.test.tsx`). The new test file follows this exact same established (if lint-violating) pattern — direct `container.querySelector` access — because there is no accessible-role way to assert raw SVG `stroke`/`fill` attribute values via Testing Library queries. This is not a regression introduced by this change; it's consistent with existing precedent for this class of assertion.

## How to verify manually

1. `cd frontend && npm start`
2. Navigate to the Bank statement import page rendering `BankStatementImportChart`.
3. Toggle the theme switcher between light and dark.
4. Confirm: grid lines stay visible on dark background, weekend highlight bands stay visible-but-subtle, the threshold reference line/label and the "below threshold" dots stay legible with a dark-appropriate outline, and the main data line remains blue in both themes.
