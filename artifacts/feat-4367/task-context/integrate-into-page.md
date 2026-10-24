### task: integrate-into-page

**Files:**
- Modify: `frontend/src/components/pages/FinancialOverview.tsx`

- [ ] Step 1: Import `FinancialSummaryCards` from `./financial-overview/FinancialSummaryCards`.
- [ ] Step 2: Replace the grid `<div>` inside `{viewMode === "timeline" && data?.summary && (...)}` under `{/* Summary Cards */}` with `<FinancialSummaryCards summary={data.summary} includeStockData={includeStockData} />`. Keep the guard and comment.
- [ ] Step 3: Remove unused lucide imports `TrendingUp`, `TrendingDown`, `Calendar`, `Package`, `BarChart3`; keep `DollarSign`, `AlertTriangle`, `ChevronDown` (still used). Verify with grep that no remaining usage exists.
- [ ] Step 4: From `frontend/`, run `npm run build`, `npm run lint`, and the tests under `src/components/pages` touching FinancialOverview and `financial-overview/__tests__`; all must pass. Confirm `FinancialOverview.tsx` shrank by roughly 140 lines.
- [ ] Step 5: Commit `refactor(financial-overview): use FinancialSummaryCards in page`.
