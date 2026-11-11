# FinancialSummaryCards Extraction Implementation Plan

**Goal:** Extract the six inline summary stat cards from `FinancialOverview.tsx` into a `FinancialSummaryCards` component with no visual or behavioural change.

**Architecture:** Presentational component with a config array and a private `SummaryCard` subcomponent, in `frontend/src/components/pages/financial-overview/`.

**Tech Stack:** React, TypeScript, Tailwind, lucide-react, Jest + React Testing Library.

### task: create-summary-cards-component

**Files:**
- Create: `frontend/src/components/pages/financial-overview/FinancialSummaryCards.tsx`
- Create: `frontend/src/components/pages/financial-overview/__tests__/FinancialSummaryCards.test.tsx`

- [ ] Step 1: Write the failing test file modelled on `FinancialDataCards.test.tsx`. Cases: four base cards with labels (Celkové příjmy, Celkové náklady, Účetní bilance, Průměrná měsíční bilance); stock cards (Změna hodnoty skladu, Celková bilance vč. skladu) shown when `includeStockData` true and `stockSummary` present; hidden when `includeStockData` false or `stockSummary` undefined; positive balance value has `text-emerald-600`, negative has `text-red-600`; average balance positive `text-blue-600`; stock change negative/undefined/0 uses `text-orange-600` and renders formatted 0; grid has `xl:grid-cols-6` with stock, `lg:grid-cols-4` without.
- [ ] Step 2: Run `cd frontend && npx jest src/components/pages/financial-overview/__tests__/FinancialSummaryCards.test.tsx` (or the repo's `npm test -- --watchAll=false <path>`); expect failure (module missing).
- [ ] Step 3: Implement the component per design.md: copy class strings verbatim from the current inline block in `FinancialOverview.tsx` (`{/* Summary Cards */}`), literal class strings only, `formatCurrency` from `./utils`, `FinancialSummaryDto` type from `../../../api/hooks/useFinancialOverview`, named export, private `SummaryCard`. Preserve `x && x >= 0` and `|| 0` semantics for stock values.
- [ ] Step 4: Re-run the test; expect pass.
- [ ] Step 5: Commit `refactor(financial-overview): add FinancialSummaryCards component`.

### task: integrate-into-page

**Files:**
- Modify: `frontend/src/components/pages/FinancialOverview.tsx`

- [ ] Step 1: Import `FinancialSummaryCards` from `./financial-overview/FinancialSummaryCards`.
- [ ] Step 2: Replace the grid `<div>` inside `{viewMode === "timeline" && data?.summary && (...)}` under `{/* Summary Cards */}` with `<FinancialSummaryCards summary={data.summary} includeStockData={includeStockData} />`. Keep the guard and comment.
- [ ] Step 3: Remove unused lucide imports `TrendingUp`, `TrendingDown`, `Calendar`, `Package`, `BarChart3`; keep `DollarSign`, `AlertTriangle`, `ChevronDown` (still used). Verify with grep that no remaining usage exists.
- [ ] Step 4: From `frontend/`, run `npm run build`, `npm run lint`, and the tests under `src/components/pages` touching FinancialOverview and `financial-overview/__tests__`; all must pass. Confirm `FinancialOverview.tsx` shrank by roughly 140 lines.
- [ ] Step 5: Commit `refactor(financial-overview): use FinancialSummaryCards in page`.
