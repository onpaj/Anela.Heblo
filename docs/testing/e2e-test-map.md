# E2E Test Map

> **Generated**: 2026-05-04
> **Scope**: All Playwright E2E spec files across all modules
> **Purpose**: Per-file inventory of test status — canonical reference for deciding what to delete, update, or keep.

## How to use this map

| Verdict | Meaning | Action |
|---------|---------|--------|
| **KEEP** | Route and selectors are current; test adds value | No change needed |
| **UPDATE** | Feature exists but selectors/assertions are stale or tests are skipped due to known issues | Fix selectors, remove skips, or rewrite stale assertions |
| **DELETE** | Feature removed, file is empty, file is a duplicate, or test is debug-only | Delete the file |

---

## Summary

| Module | Files | KEEP | UPDATE | DELETE |
|--------|------:|-----:|-------:|-------:|
| catalog | 9 | 7 | 2 | 0 |
| issued-invoices | 6 | 4 | 1 | 1 |
| stock-operations | 9 + 1 orphan | 9 | 0 | 1 |
| transport | 7 | 7 | 0 | 0 |
| manufacturing | 5 | 5 | 0 | 0 |
| marketing | 5 | 5 | 0 | 0 |
| core | 10 | 5 | 3 | 2 |
| **Total** | **52** | **42** | **6** | **4** |

---

## catalog module

| File | Verdict | Test count | Reason |
|------|---------|-----------:|--------|
| `clear-filters.spec.ts` | KEEP | 8 | Route `/catalog` and selectors current; 1 test skipped for a documented race condition (pagination reset) that was already fixed. |
| `combined-filters.spec.ts` | KEEP | 7 | Multi-filter logic sound; one skipped test reflects a real app bug (pagination doesn't reset on filter change), not a stale test. |
| `filter-edge-cases.spec.ts` | KEEP | 11 | Route and selectors valid; edge case coverage solid and uses fixture-based test data. |
| `margins-chart.spec.ts` | KEEP | 1 | Tests Marże tab in detail modal; fixture-based, selector current. |
| `pagination-with-filters.spec.ts` | UPDATE | 13 | Route exists but 5 tests document known pagination-reset bugs with assertions that accommodate the buggy behavior; need reconciliation once the underlying race condition is fixed. |
| `product-type-filter.spec.ts` | KEEP | 3 | Route and selectors current (`select#productType`); all pass. |
| `sorting-with-filters.spec.ts` | KEEP | 9 | Route and selectors valid; 1 test skipped for a real UX bug (sort column change while on page 2 doesn't reset pagination), not a stale selector. |
| `text-search-filters.spec.ts` | UPDATE | 15 | Route exists; 2 tests skipped — one for a race condition fix already applied to `CatalogList.tsx`, one for combined name+code filters that may not be supported by the backend simultaneously. |
| `ui.spec.ts` | KEEP | 1 | Basic catalog page load test; comprehensive fallback selectors; route confirmed. |

### Notes
- `pagination-with-filters.spec.ts`: Failures stem from a React Query + `useEffect` race condition where clicking page 2 immediately triggers a refetch that resets to page 1. Tests currently assert the buggy state. Once the root cause is fixed in `CatalogList.tsx`, the assertions need updating.
- `text-search-filters.spec.ts`: The combined name+code text filter test (lines 317–370) is skipped because the backend may not support both text filters active simultaneously. Before deleting the skip, verify backend behavior; if intentional, the test should be deleted.
- `sorting-with-filters.spec.ts` / `clear-filters.spec.ts` / `combined-filters.spec.ts`: Skipped tests document real app bugs. Consider tracking these in a bug backlog rather than leaving them as `.skip()` forever.

---

## issued-invoices module

| File | Verdict | Test count | Reason |
|------|---------|-----------:|--------|
| `filters.spec.ts` | UPDATE | 9 | 6 of 9 tests are `.skip()`-ed due to upstream navigation issues that were resolved in the navigation helper; the file needs the skips removed and the tests re-validated. |
| `import-modal.spec.ts` | DELETE | 14 | All 14 tests are skipped and test a file-upload modal that does not exist; the real import modal is a date-range picker (radio buttons + date fields), not a file uploader. |
| `navigation.spec.ts` | KEEP | 2 | Both tests pass; navigation helper fixed; route and selectors confirmed. |
| `pagination.spec.ts` | KEEP | 7 | All 7 tests pass; selectors (`nav[aria-label="Pagination"]`, `select#pageSize`) verified. |
| `sorting.spec.ts` | KEEP | 7 | All 7 tests pass; column headers verified in `IssuedInvoicesPage.tsx`. |
| `status-badges.spec.ts` | KEEP | 4 | All 4 tests pass; badge pattern `/Synced|Chyba|Čeká/` verified in component. |

### Notes
- `import-modal.spec.ts`: **DELETE.** The expected UI (drag-and-drop file upload, `input[type="file"]`, "Nahrát" button) does not exist. The actual import modal is API-based date-range import. Rewriting these tests would mean writing an entirely new test file — clean delete is better.
- `filters.spec.ts`: The skips were caused by a navigation helper issue that is now fixed in `e2e-auth-helper.ts`. Remove `.skip()` calls, run on staging, and verify all 9 tests pass before treating as KEEP.

---

## stock-operations module

| File | Verdict | Test count | Reason |
|------|---------|-----------:|--------|
| `accept.spec.ts` | KEEP | 3 | Route confirmed, dialog text "selhanou operaci" is correct current text, all tests passing. |
| `badges.spec.ts` | KEEP | 7 | Route confirmed; badge selectors (SVG icons, class names) current; validates badge colors and "stuck" warnings. |
| `filters.spec.ts` | KEEP | 21 | Comprehensive filter coverage (product, document, dates, combined); selectors use role-based and input patterns; all critical UI paths covered. |
| `navigation.spec.ts` | KEEP | 5 | Tests page load, default filters, empty state, error state with retry button; selectors verified. |
| `panel.spec.ts` | KEEP | 6 | Tests filter panel collapse/expand, refresh, filter persistence; selectors match component. |
| `retry.spec.ts` | KEEP | 6 | Tests retry buttons for Failed/Submitted/Pending states; color validation (`bg-red-600`, `bg-orange-500`, `bg-yellow-600`); dialog confirmation. |
| `sorting.spec.ts` | KEEP | 3 | Tests column sorting; `sortByColumn` helper updated per FAILED_TESTS.md; chevron icon validation. |
| `source-filter.spec.ts` | KEEP | 3 | Tests source type filters (All, TransportBox, GiftPackageManufacture); radio button selectors valid. |
| `state-filter.spec.ts` | KEEP | 6 | Tests all state filters with badge validation; FAILED_TESTS.md confirms fixes applied. |
| `../stock-operations-accept.spec.ts` *(orphan)* | DELETE | 3 | Duplicate of `accept.spec.ts` with a stale assertion: line 49 uses outdated dialog text "chybnou operaci" (incorrect) vs the correct "selhanou operaci". Not picked up by Playwright config (outside module dir). |

### Notes
- `../stock-operations-accept.spec.ts` (root orphan): This file is **not executed** by the module-based Playwright config (the runner scans `stock-operations/` not the root). It has stale dialog text that was already fixed in the module version. Safe to delete without losing any coverage.

---

## transport module

| File | Verdict | Test count | Reason |
|------|---------|-----------:|--------|
| `box-creation.spec.ts` | KEEP | 5 | Route `/logistics/transport-boxes` confirmed; selectors ("Otevřít nový box", h1) verified in `TransportBoxList.tsx`; 43/43 module tests passing. |
| `box-items.spec.ts` | KEEP | 7 | Routes confirmed; selectors align with `TransportBoxList`/Detail components; multiple fallback selectors make tests resilient. |
| `box-management.spec.ts` | KEEP | 7 | Transport routes active; page elements (table, search box, status badges in Czech) match component; all passing. |
| `box-receive.spec.ts` | KEEP | 6 | Dedicated route `/logistics/receive-boxes` confirmed; h1 "Příjem transportních boxů" verified in component; input selectors current. |
| `box-workflow.spec.ts` | KEEP | 6 | Routes functional; state transitions tested across components; selectors permissive and well-designed. |
| `boxes-basic.spec.ts` | KEEP | 5 | Navigation verified (App.tsx lines 416–422); component selectors accurate; state badge expectations valid. |
| `ean-integration.spec.ts` | KEEP | 7 | All transport routes confirmed; EAN scanning/validation coverage comprehensive; component source files match test expectations. |

### Notes
- Transport module is in excellent health: 43/43 tests passing, all routes and selectors current. No action required.
- Routes confirmed in `App.tsx` lines 416–422: `/logistics/transport-boxes` → `TransportBoxList`, `/logistics/receive-boxes` → `TransportBoxReceive`.

---

## manufacturing module

| File | Verdict | Test count | Reason |
|------|---------|-----------:|--------|
| `batch-planning-error-handling.spec.ts` | KEEP | 2 | Route `/manufacturing/batch-planning` confirmed; selectors (combobox, table checkboxes/spinbuttons, Calculate button) verified; 7/7 module tests passing. |
| `batch-planning-workflow.spec.ts` | KEEP | 3 | Route confirmed; comprehensive workflow (product selection → quantity modification → order creation) matches actual component props and selectors. |
| `order-creation.spec.ts` | KEEP | 1 | Routes `/manufacturing/batch-planning` and `/manufacturing/batch-calculator` confirmed; workflow test follows actual creation flow. |
| `order-state-return.spec.ts` | KEEP | 2 | Route `/manufacturing/orders` confirmed; state transition tests use stable selectors (button roles, table rows with `MO-` prefix). |
| `protocol.spec.ts` | KEEP | 2 | Recently added for Protocol PDF feature; `getByTitle('Tisknout protokol výroby')` matches `DetailActionButtons.tsx` line 93 exactly; conditional rendering tested correctly. |

### Notes
- Manufacturing module: 7/7 tests passing. All 5 files are current (including `protocol.spec.ts` which was not in the original module guide — **add it to `e2e-module-guide.md`**).
- Routes confirmed in App.tsx: `/manufacturing/batch-planning`, `/manufacturing/batch-calculator`, `/manufacturing/orders`.

---

## marketing module

> ⚠️ **This module is not listed in `docs/testing/e2e-module-guide.md`** — needs to be added (5 spec files, estimated 2–3 min runtime).

| File | Verdict | Test count | Reason |
|------|---------|-----------:|--------|
| `calendar-view.spec.ts` | KEEP | 7 | Route `/marketing/calendar` confirmed; selectors (Kalendář toggle, Czech weekday abbreviations, event bars, modal buttons) verified in `MarketingCalendarPage.tsx`. |
| `create-record.spec.ts` | KEEP | 7 | Route and modal components fully functional; selectors ("Nová akce", "Vytvořit", action type dropdown, form fields) verified in source. |
| `grid-view.spec.ts` | KEEP | 9 | "Seznam" tab and table structure verified in `MarketingActionGrid.tsx`; filters and pagination components exist. |
| `leaflet-generator.spec.ts` | KEEP | 1 | Route `/leaflet-generator` confirmed in App.tsx; selectors verified in `LeafletForm.tsx` and `LeafletResult.tsx` (h1, "Téma" label, radio buttons, "Vygenerovat leták", "Kopírovat"/"Zkopírováno"); feature is in main branch. |
| `loading.spec.ts` | KEEP | 5 | Tests marketing page loading and initial state; heading "Marketingový kalendář", sidebar nav, view toggle all verified. |

### Notes
- Module not mentioned in `FAILED_TESTS.md` (snapshot was taken before this module existed).
- Routes in App.tsx: `/marketing/calendar` → `MarketingCalendarPage` (line 392–394), `/leaflet-generator` → `LeafletGeneratorPage` (line 396–398).
- `leaflet-generator.spec.ts` is on the current feature branch but the underlying components exist in `frontend/src/features/leaflet-generator/` — treat as KEEP once merged to main.
- **Action**: Add `marketing/` to `docs/testing/e2e-module-guide.md` and to the GitHub Actions workflow matrix.

---

## core module

| File | Verdict | Test count | Reason |
|------|---------|-----------:|--------|
| `changelog.spec.ts` | KEEP | 11 | Route `/` confirmed; `data-testid="changelog-modal"` selector current; tests verify modal lifecycle and content. |
| `dashboard.spec.ts` | KEEP | 7 | Route `/dashboard` confirmed; `data-testid="dashboard-container"` and `data-testid^="dashboard-tile-"` selectors current. |
| `debug-transport-page.spec.ts` | DELETE | 1 | Debug utility masquerading as a test: only logs page state and always passes (`expect(true).toBe(true)`). Not a regression test. |
| `gift-package-disassembly.spec.ts` | DELETE | 0 | Empty file — only a `beforeEach` hook, no `test()` cases. Route exists but no test body was ever written. |
| `invoice-classification-history.spec.ts` | UPDATE | 1 | Route exists; single pagination test is overly defensive with 5 nested conditionals; refactor to use clear page-state assertions. |
| `invoice-classification-history-actions.spec.ts` | UPDATE | 13 | Route exists; 10 of 13 tests are skipped — they expect a classification modal that doesn't exist (the "Klasifikovat" button calls the API directly, not a modal). The 3 unskipped rule-creation tests pass. |
| `invoice-classification-history-filters.spec.ts` | UPDATE | 9 | Route exists; 7 of 9 tests pass after column-index fix (FAILED_TESTS.md); 2 tests skipped for a real app bug (backend filter is case-sensitive, tests expect case-insensitive). |
| `recurring-jobs-management.spec.ts` | KEEP | 25 | Route `/recurring-jobs` confirmed; all 12 expected job names verified against backend job classes; selectors current; toggle/refresh/cron tests cover full feature. |
| `sidebar-navigation.spec.ts` | KEEP | 3 | Route `/` confirmed; `getByRole` selectors current; tests verify Personální section nav and external orgchart link. |
| `staging-auth.spec.ts` | KEEP | 3 | Route `/` confirmed; tests verify auth flow (API endpoint, dashboard load, session flag); real smoke test for E2E auth setup. |

### Notes
- `debug-transport-page.spec.ts`: **DELETE.** The single test body is `expect(true).toBe(true)` — it always passes and provides no regression value. This is a leftover debugging tool.
- `gift-package-disassembly.spec.ts`: **DELETE.** Zero test cases. Only a `beforeEach` navigating to the page. File was started but never completed. The route exists — if coverage is desired, write a new test file from scratch.
- `invoice-classification-history-actions.spec.ts`: The 10 skipped tests expect a modal form triggered by "Klasifikovat". The actual implementation calls `classifySingleInvoiceMutation.mutateAsync()` directly with no modal. These skipped tests cannot pass without a significant app change. Options: (a) delete the 10 skipped tests and only keep the 3 rule-creation tests, or (b) rewrite to test the actual button + spinner behavior.
- `invoice-classification-history-filters.spec.ts`: 2 case-insensitive filter tests are blocked by a backend bug, not a stale selector. Treat as technical debt — either fix the backend or document and delete the tests.

---

## Follow-up actions

### Delete (4 files)
1. `frontend/test/e2e/stock-operations-accept.spec.ts` — duplicate orphan, stale dialog text, not in Playwright config
2. `frontend/test/e2e/issued-invoices/import-modal.spec.ts` — tests a non-existent file-upload feature (14 tests, all skipped)
3. `frontend/test/e2e/core/debug-transport-page.spec.ts` — debug artifact with always-passing assertion
4. `frontend/test/e2e/core/gift-package-disassembly.spec.ts` — empty file (0 test cases)

### Update (6 files)
1. `frontend/test/e2e/catalog/pagination-with-filters.spec.ts` — reconcile assertions once pagination race condition is fixed
2. `frontend/test/e2e/catalog/text-search-filters.spec.ts` — resolve 2 skipped tests (race condition already fixed; combined-filter behavior needs backend verification)
3. `frontend/test/e2e/issued-invoices/filters.spec.ts` — remove `.skip()` from 6 tests and re-validate on staging
4. `frontend/test/e2e/core/invoice-classification-history.spec.ts` — simplify overly defensive pagination assertions
5. `frontend/test/e2e/core/invoice-classification-history-actions.spec.ts` — delete the 10 skipped classification-modal tests; keep the 3 rule-creation tests
6. `frontend/test/e2e/core/invoice-classification-history-filters.spec.ts` — either fix backend case-sensitivity or delete the 2 permanently-skipped tests

### Documentation updates (not test changes)
- Add `marketing/` module to `docs/testing/e2e-module-guide.md` (5 files, 2–3 min estimated runtime)
- Add `protocol.spec.ts` to manufacturing module listing in `e2e-module-guide.md`
- Add `marketing` to the GitHub Actions nightly workflow matrix

---

## References
- Module structure: `docs/testing/e2e-module-guide.md`
- Nightly failure history: `frontend/test/e2e/FAILED_TESTS.md`
- Test runner: `scripts/run-playwright-tests.sh`
- Test data fixtures: `docs/testing/test-data-fixtures.md`
- Auth helpers: `frontend/test/e2e/helpers/e2e-auth-helper.ts`
