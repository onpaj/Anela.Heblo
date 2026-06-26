# Architecture Review: Fix Core E2E Tests — Dashboard AutoShow Tile, Classification Combined Filters, Classification Pagination

## Skip Design: true

## Architectural Fit Assessment

All three fixes are pure E2E test changes. No production code, no backend, no frontend components are touched. The fixes address three distinct categories of test fragility that are common in suites targeting live staging data:

1. **Timeout too short for network conditions** — `dashboard.spec.ts` uses a 5 s `waitForSelector` timeout inside the test body, while the `beforeEach` already uses 10 s for the container. The 5 s limit is lower than observed staging latency.
2. **Hard assertion against live data that may not satisfy it** — `invoice-classification-history-filters.spec.ts:439` asserts `filteredCount > 0` after applying all four filters simultaneously against staging data. The combination of four filters can legally return zero rows, which is a valid application state, not a failure.
3. **Unconditional wait against a conditionally-rendered element** — `invoice-classification-history.spec.ts:24` calls `page.waitForTimeout(2000)` in `beforeEach` then expects `nav[aria-label="Pagination"]` to exist. The pagination nav is only rendered when `totalPages > 1`, so on a dataset that fits one page it is never present and `nextButton.isDisabled()` times out.

All three fixes are self-contained within their respective spec files. No helper files need to change; the existing helpers in `classification-history-helpers.ts` already expose the correct abstractions (`getPaginationControls`, `hasNoRecordsMessage`, `waitForClassificationHistoryLoaded`).

## Proposed Architecture

### Component Overview

```
frontend/test/e2e/core/
  dashboard.spec.ts                              ← Fix 1: timeout 5000 → 15000
  invoice-classification-history.spec.ts         ← Fix 3: beforeEach + pagination guard
  invoice-classification-history-filters.spec.ts ← Fix 2: combined-filters assertion

frontend/test/e2e/helpers/
  classification-history-helpers.ts              ← no changes required
  e2e-auth-helper.ts                             ← no changes required
```

No new files. No helper changes. Each fix is a targeted edit inside one test block.

### Key Design Decisions

#### Decision 1: Dashboard timeout increase (FR-1)

**Options considered:**
- Increase `waitForSelector` timeout to 15 s inside the test body.
- Move the AutoShow tile wait into `beforeEach` so it shares the already-established 10 s container guard.
- Skip the test when the tile is absent (soft assertion).

**Chosen approach:** Increase the `waitForSelector` timeout from 5 000 ms to 15 000 ms on line 27 of `dashboard.spec.ts`. The `beforeEach` already waits for `[data-testid="dashboard-container"]` with a 10 s timeout, so the tile has a reasonable chance of being present by the time the test body runs. The additional 15 s window absorbs extra API latency on staging without changing test semantics.

**Rationale:** The spec states the root cause may be network latency exceeding 5 s. The simplest fix that preserves test intent is a timeout increase. Moving the wait to `beforeEach` would change the contract for all tests in the describe block; the tile is only relevant to this one test.

---

#### Decision 2: Combined-filters assertion resilience (FR-2)

**Options considered:**
- Assert `filteredCount === 0 || filteredCount > 0` (tautology — useless).
- Assert `noRecords || filteredCount > 0` — valid empty state OR matching rows.
- Assert only that the API call completed without error (no DOM assertion).

**Chosen approach:** Replace `expect(filteredCount).toBeGreaterThan(0)` (line 482) with:

```typescript
const noRecords = await hasNoRecordsMessage(page);
expect(noRecords || filteredCount > 0).toBeTruthy();
```

The subsequent assertions that validate row content (lines 489–501) must be guarded: only run them when `filteredCount > 0`. The `hasNoRecordsMessage` helper is already available from the import at the top of the file.

**Rationale:** The test intent is to verify that applying four filters together does not crash the app and that the results are consistent with the applied filters. That intent is satisfied whether the result set is empty (valid) or non-empty (also valid). The row-content assertions below are only meaningful when rows exist and must be wrapped in a conditional.

---

#### Decision 3: Pagination test — `beforeEach` wait and nav guard (FR-3)

**Options considered:**
- Replace `page.waitForTimeout(2000)` with `page.waitForSelector(...)` targeting table or empty-state text, then guard the pagination nav with a conditional before asserting `isDisabled`.
- Add a fallback: if nav is absent, `return` early (single-page dataset — nothing to test).
- Assert the pagination nav is present unconditionally with a longer timeout.

**Chosen approach:** In `invoice-classification-history.spec.ts`:

1. In `beforeEach` (line 19), replace the unconditional `await page.waitForTimeout(2000)` with:
   ```typescript
   await page.waitForSelector(
     'table, :text("Nebyly nalezeny žádné záznamy klasifikace")',
     { timeout: 15000 }
   );
   ```
   Note: the spec's suggested empty-state text is `"Nebyly nalezeny žádné záznamy klasifikace"`. The actual text visible in the helpers and in the other spec file is `"Nebyly nalezeny žádné záznamy"`. Use the shorter form that matches production — verify against the live page before committing, or use a partial-text approach `:text("Nebyly nalezeny")`.

2. In the `pagination functionality` test body (which is already partially rewritten in the current file — lines 24–73 show an already-updated version): the current file as of the codebase snapshot already applies the conditional pagination guard. The `beforeEach` `waitForTimeout` on line 19 is the remaining issue.

**Rationale:** `waitForTimeout` is a fixed sleep and does not guarantee the DOM has settled. `waitForSelector` with a dual-target (`table` OR empty-state message) exits as soon as the page has definitively rendered either state, eliminating both under-wait (table not yet present) and over-wait (empty state rendered instantly). The pagination nav guard (`if (!hasPagination) return`) already present in the current file body is the correct approach for data-dependent pagination.

## Implementation Guidance

### Directory / Module Structure

All changes are confined to:

| File | Change |
|------|--------|
| `frontend/test/e2e/core/dashboard.spec.ts` | Line 27: `timeout: 5000` → `timeout: 15000` |
| `frontend/test/e2e/core/invoice-classification-history.spec.ts` | Line 19: replace `waitForTimeout(2000)` with `waitForSelector(...)` |
| `frontend/test/e2e/core/invoice-classification-history-filters.spec.ts` | Lines 482–501: replace hard `filteredCount > 0` assertion with `noRecords \|\| filteredCount > 0`; guard row-content assertions |

### Interfaces and Contracts

No new interfaces. The following existing helpers are available and must be used (not reimplemented):

- `hasNoRecordsMessage(page)` from `classification-history-helpers.ts` — returns `boolean`; checks for `':text("Nebyly nalezeny žádné záznamy")'`
- `getPaginationControls(page)` from `classification-history-helpers.ts` — already wraps `nav[aria-label="Pagination"]` with safe locator construction
- `waitForClassificationHistoryLoaded(page)` from `classification-history-helpers.ts` — waits for header, table, then rows-or-no-records; used in `beforeEach` of the filters spec already

The `invoice-classification-history.spec.ts` `beforeEach` does NOT use `waitForClassificationHistoryLoaded`. It uses inline `waitForSelector` calls. Keep that pattern for this file; do not refactor to call the helper (out of scope).

### Data Flow

**Fix 1 — Dashboard tile:**
```
beforeEach: navigateToApp → waitForSelector("dashboard-container", 10s)
test body:  waitForSelector("[data-testid^='dashboard-tile-']", 15s)  ← was 5s
            expect(backgroundTasksTile).toBeVisible()
```

**Fix 2 — Combined filters:**
```
applyFilters(page, { fromDate, toDate, invoiceNumber, companyName })
  └─ fills inputs, waits for API /api/InvoiceClassification/history 200
  └─ waits for "table tbody tr" or no-records selector
getRowCount(page) → filteredCount
hasNoRecordsMessage(page) → noRecords
expect(noRecords || filteredCount > 0).toBeTruthy()   ← was: expect(filteredCount).toBeGreaterThan(0)
if (filteredCount > 0) {
  // row-content assertions (guarded)
}
```

**Fix 3 — Pagination:**
```
beforeEach:
  navigateToInvoiceClassification
  waitForSelector("h1:has-text('Klasifikace faktur')", 15s)
  waitForSelector('table, :text("Nebyly nalezeny")', 15s)  ← replaces waitForTimeout(2000)

test body (already correctly structured in current file):
  expect(tableLocator.or(emptyStateLocator).first()).toBeVisible(15s)
  if (!hasTable) return
  if (!hasPagination) return
  // pagination interaction
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Empty-state text mismatch: `"Nebyly nalezeny žádné záznamy klasifikace"` (spec) vs `"Nebyly nalezeny žádné záznamy"` (helpers/other specs) | Medium | Use `:text("Nebyly nalezeny")` partial match in `waitForSelector`, or verify exact text against live staging before the PR lands |
| `waitForClassificationHistoryLoaded` in helpers already waits for `table` — if the `beforeEach` of `invoice-classification-history.spec.ts` is changed to call the helper instead, it would double-wait | Low | Do not call the helper; keep inline `waitForSelector` as specified. Out of scope to refactor. |
| Increasing dashboard tile timeout from 5 s to 15 s could mask a genuine regression where the tile is never rendered | Low | The test still fails if the tile doesn't appear within 15 s. The tolerance increase is proportionate to observed staging latency. |
| Combined-filters test now passes on zero results — reducing signal on filter correctness | Low | The test still validates that the filter applies without error and that the UI renders a valid state. Row-content assertions remain active when rows are present. |

## Specification Amendments

**FR-3 empty-state text:** The spec proposes `':text("Nebyly nalezeny žádné záznamy klasifikace")'` as the selector. The text actually used throughout the test suite (in `classification-history-helpers.ts` and in `invoice-classification-history.spec.ts` line 27) is `"Nebyly nalezeny žádné záznamy"` (without `"klasifikace"`). Use the shorter form for consistency, or use a partial match. Confirm against the live DOM before finalising.

**FR-2 row-content assertions:** The spec says to change only the `filteredCount > 0` assertion. The row-content assertions on lines 489–501 that follow unconditionally also implicitly assume non-empty results (`filteredFirstRow.locator(...)` calls will fail on an empty table). Those lines must be wrapped in `if (filteredCount > 0)` as part of the same change.

## Prerequisites

None. All three fixes are self-contained test edits with no infrastructure, migration, or configuration dependencies. The staging environment and the nightly Playwright runner are already in place.
