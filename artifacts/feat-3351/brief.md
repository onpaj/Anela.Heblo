**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **3** core failures.

Three small, independent items:

- [ ] **Dashboard AutoShow tile** (`frontend/test/e2e/core/dashboard.spec.ts:25`): `[data-testid="dashboard-tile-backgroundtaskstatus"]` not visible → the tile was renamed/removed or no longer AutoShows. Verify the tile's `data-testid` and AutoShow config; update test or restore tile.
- [ ] **Classification combined filters** (`frontend/test/e2e/core/invoice-classification-history-filters.spec.ts:439`): filtered count = 0 → no staging row matches all four filters. Seed matching data or relax the "all four together" assertion to tolerate empty results.
- [ ] **Classification pagination** (`frontend/test/e2e/core/invoice-classification-history.spec.ts:24`): `nextButton.isDisabled()` times out 30 s → pagination control locator not found / page didn't finish loading. Fix the pagination selector / load wait.

## Acceptance criteria
- All three core tests pass (or are documented as data-dependent with a robust assertion).
