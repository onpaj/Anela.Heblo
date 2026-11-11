# Specification: Extract FinancialSummaryCards component

## Summary
`FinancialOverview.tsx` renders six near-identical summary stat cards inline (the `{/* Summary Cards */}` block). Extract them into a presentational `FinancialSummaryCards` component driven by a config array so each card is defined once. This is a pure refactor: rendered DOM, classes, labels and behaviour must not change.

## Background
All other sub-views of the page (`FinancialFilters`, `FinancialChart`, `FinancialDataTable`, `FinancialDataCards`, `FinancialComparisonChart`, `FinancialComparisonTable`) already live in `frontend/src/components/pages/financial-overview/`. The summary row is the only unextracted section (~170 lines of JSX), which makes the page 584 lines and forces six edits for any card-structure change. Origin: arch-review issue #4367.

## Functional Requirements

### FR-1: New component
Create `frontend/src/components/pages/financial-overview/FinancialSummaryCards.tsx` exporting named `FinancialSummaryCards: React.FC<{ summary: FinancialSummaryDto; includeStockData: boolean }>`.
**Acceptance criteria:**
- Renders the outer grid with the same className logic as today (`grid grid-cols-2 md:grid-cols-2 {xl:grid-cols-6 | lg:grid-cols-4} gap-4 mb-6`).
- Cards are built from a config array of `{ key, icon, iconClass, label, value, valueClass }` and mapped once to a single card markup.

### FR-2: Preserve the six cards exactly
Order and behaviour:
1. Celkové příjmy: TrendingUp, `text-emerald-400`, value `totalIncome`, neutral value colour (`text-gray-900 dark:text-graphite-text`).
2. Celkové náklady: TrendingDown, `text-red-400`, value `totalExpenses`, neutral value colour.
3. Účetní bilance: DollarSign, `totalBalance >= 0` emerald-400/red-400 icon, emerald-600 dark:emerald-400 / red-600 dark:red-400 value.
4. Průměrná měsíční bilance: Calendar, `averageMonthlyBalance >= 0` blue-400/red-400 icon, blue-600 dark:blue-400 / red-600 dark:red-400 value.
5. Změna hodnoty skladu (only if `includeStockData && summary.stockSummary`): Package, purple-400/orange-400 icon, purple-600 dark:purple-400 / orange-600 dark:amber-400 value, using `totalStockValueChange`; falsy treated as 0 for formatting and as negative for colour (existing behaviour: `x && x >= 0`).
6. Celková bilance vč. skladu (same condition): BarChart3, emerald/red, using `totalBalanceWithStock`, same falsy handling.
**Acceptance criteria:**
- Class strings, label text and `formatCurrency` output are identical to the current implementation.
- Cards 5 and 6 are not rendered when `includeStockData` is false or `stockSummary` is missing.

### FR-3: Page integration
In `FinancialOverview.tsx` replace the inline block with `{viewMode === "timeline" && data?.summary && <FinancialSummaryCards summary={data.summary} includeStockData={includeStockData} />}`; remove now-unused imports (`TrendingUp`, `TrendingDown`, `Calendar`, `Package`, `BarChart3`; keep `DollarSign`, `AlertTriangle`, `ChevronDown`, which are used elsewhere).
**Acceptance criteria:**
- `npm run build` and `npm run lint` pass with no unused import warnings.
- Existing FinancialOverview tests (if any) still pass.

### FR-4: Tests
Add `financial-overview/__tests__/FinancialSummaryCards.test.tsx` following existing sibling tests (React Testing Library, Jest).
**Acceptance criteria:**
- Covers: four base cards render with labels; stock cards shown/hidden by `includeStockData`; stock cards hidden when `stockSummary` undefined; positive vs negative colour classes for balance cards; grid class switches between `xl:grid-cols-6` and `lg:grid-cols-4`; zero/undefined stock values.

## Non-Functional Requirements

### NFR-1: Performance
No change; component is cheap and stateless. No memoisation required.

### NFR-2: Security
None; no data or auth change.

## Data Model
No change. Props use `FinancialSummaryDto` (re-exported from `api/hooks/useFinancialOverview`).

## API / Interface Design
Props: `summary: FinancialSummaryDto`, `includeStockData: boolean`. No API or generated-client changes.

## Dependencies
`lucide-react`, `formatCurrency` from `./utils`, `FinancialSummaryDto` type from `../../../api/hooks/useFinancialOverview`.

## Out of Scope
- Loading skeletons, tooltips, or any visual change.
- Extracting other parts of the page (data fetching, chart data construction).
- Backend and process docs (no process/sync/calculation changes).

## Open Questions
None.

## Status: COMPLETE
