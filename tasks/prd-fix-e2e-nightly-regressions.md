# PRD: Fix E2E Nightly Regression Failures (Feb 2026)

## Introduction

10 E2E tests in the nightly regression suite are currently failing. The failures span three files
and three distinct root causes. This PRD breaks the work into focused investigation and fix stories
so that each root cause is understood before code is changed. Increasing timeouts is not an option —
every fix must address the underlying cause.

**Constraint:** Do not increase any `waitForTimeout` or `waitForResponse` timeout values as a fix.

---

## Goals

- All 10 failing E2E tests pass on the next nightly run
- No new test instability introduced by the fixes
- Root causes documented in code comments so the same class of issue is avoided in future

---

## User Stories

### US-001: Analyze root cause of catalog text-search-filter timeouts

**Description:** As a developer, I need to understand why all 8 tests in
`catalog/text-search-filters.spec.ts` time out on `page.waitForResponse`, so I can fix the correct
thing instead of guessing.

**Context:**

All 8 failing catalog tests call `waitForTableUpdate(page)` after a filter has already been applied.
`waitForTableUpdate` → `waitForSearchResults` → `page.waitForResponse('/api/catalog', timeout: 15000)`.
The `applyProductNameFilter` / `applyProductCodeFilter` helpers already wait for loading to complete
BEFORE returning, so by the time `waitForTableUpdate` is called the API response may have already
fired. If that is the case, `page.waitForResponse` is registered too late and will never see the
response, causing a 15 s timeout.

**Investigation steps:**
1. Read `frontend/test/e2e/helpers/wait-helpers.ts` → `waitForSearchResults` (already done)
2. Read `frontend/test/e2e/helpers/catalog-test-helpers.ts` → `applyProductNameFilter` (already done)
3. Trace the exact sequence: fill → click → `waitForLoadingComplete` → return → test calls
   `waitForTableUpdate`. Confirm whether the catalog API response fires before or after the
   `waitForResponse` registration.
4. Check the catalog React component to understand when loading indicators appear/disappear:
   - Search for the `data-loading`, `aria-busy`, `.loading`, `.spinner` selectors in the frontend
   - Determine if the loading indicator exists at all in the catalog page, or if
     `waitForLoadingComplete` returns immediately (because no matching element is found)
5. Run a local staging trace: add `console.log` timestamps around the `page.waitForResponse`
   call and the filter action to confirm the race condition hypothesis.
6. Document the root cause with a comment in the relevant helper.

**Expected output:** A short written summary (as a comment in the code or in a follow-up note)
stating: "Root cause is X. Fix is Y." This feeds directly into US-002.

**Acceptance Criteria:**
- [ ] Trace the exact code path from filter click to `page.waitForResponse` registration
- [ ] Identify whether `waitForLoadingComplete` returns before or after the catalog API response arrives
- [ ] Check if any loading indicator selector (`[data-loading="true"]`, `.loading`, `.spinner`, `[aria-busy="true"]`) is actually rendered by the catalog page
- [ ] Confirm (or disprove) the race-condition hypothesis: `page.waitForResponse` registered after API response already fired
- [ ] Document findings as a comment block at the top of `waitForSearchResults` in `wait-helpers.ts`

---

### US-002: Fix the catalog text-search-filter timeout failures

**Description:** As a developer, I want all 8 catalog text-search-filter tests to pass without
increasing timeouts, so that the nightly suite is green.

**Depends on:** US-001 (root cause must be understood first)

**Likely fix (based on US-001 investigation):**

The `waitForSearchResults` function registers `page.waitForResponse` after the action that triggers
the request has already been sent and may have returned. The correct pattern is to set up
`page.waitForResponse` BEFORE the triggering action using `Promise.all`, or to remove the redundant
`waitForResponse` call from `waitForTableUpdate` entirely if `waitForLoadingComplete` already
guarantees the response has arrived.

Option A — Register listener before action (in the apply helpers):
```typescript
// In applyProductNameFilter:
const [response] = await Promise.all([
  page.waitForResponse(resp => resp.url().includes('/api/catalog') && resp.status() === 200),
  filterButton.click(),
]);
```

Option B — Remove `waitForResponse` from `waitForTableUpdate`, rely solely on UI state:
```typescript
export async function waitForTableUpdate(page: Page): Promise<void> {
  await waitForLoadingComplete(page, { timeout: 15000 });
  await page.waitForTimeout(300); // minimal stabilisation only
}
```

Choose the option that matches the actual root cause discovered in US-001.

**Acceptance Criteria:**
- [ ] All 8 failing tests in `catalog/text-search-filters.spec.ts` pass locally against staging
- [ ] No other catalog tests regress (run full catalog module: `./scripts/run-playwright-tests.sh catalog`)
- [ ] No timeout values were increased
- [ ] Fix is in the shared helper(s), not duplicated per test
- [ ] `dotnet build` and `npm run build` still pass (if any frontend code was changed)

---

### US-003: Fix dashboard "should display AutoShow tiles automatically" failure

**Description:** As a developer, I want the dashboard AutoShow tile test to pass so that the
background task status tile is verified to be visible on the dashboard.

**Error:**
```
Locator: locator('[data-testid="dashboard-tile-backgroundtaskstatus"]').locator('.text-sm.font-medium')
Expected substring: "Stav background tasků"
Timeout: 5000ms
Error: element(s) not found
```

The test waits for `[data-testid="dashboard-tile-backgroundtaskstatus"]` to exist and contain
`.text-sm.font-medium` text. The element is not found, meaning either:
- The `BackgroundTaskStatusTile` no longer has `AutoShow: true`, so it is not rendered by default
- The `data-testid` naming convention changed (e.g. tile ID is now different)
- The CSS class `.text-sm.font-medium` was removed or renamed in the tile component
- The tile was removed from the application entirely

**Investigation steps before fixing:**
1. Find the `BackgroundTaskStatusTile` class in the frontend source
2. Verify if it still has an `AutoShow` property set to `true`
3. Check how `data-testid` is generated for dashboard tiles (the test assumes
   `backgroundtaskstatus` = class name lowercased with spaces stripped)
4. Check the tile's template/render for `.text-sm.font-medium` class usage
5. Confirm whether the tile is still active and expected to appear by default

**Fix options (choose based on investigation):**
- A. If `AutoShow` was removed: restore it, OR update the test to reflect that the tile is
     opt-in and must be enabled via settings before testing
- B. If `data-testid` naming changed: update the selector in the test to match current convention
- C. If CSS class changed: update the locator `.text-sm.font-medium` to the current class

**Acceptance Criteria:**
- [ ] Test `should display AutoShow tiles automatically` passes against staging
- [ ] No other dashboard tests regress (run: `./scripts/run-playwright-tests.sh core`)
- [ ] If the tile was intentionally removed, the test is updated to test the actual AutoShow tile(s)
- [ ] Root cause documented as a comment in the test file

---

### US-004: Fix invoice classification "should apply all four filters together" failure

**Description:** As a developer, I want the combined-filter test for invoice classification
history to pass so that the date + invoice + company name filter combination is verified.

**Error:**
```
expect(received).toBeGreaterThanOrEqual(expected)
Expected: >= 2
Received: 1
```

**Root cause (identified from code reading):**

At line 471 of `invoice-classification-history-filters.spec.ts`:
```typescript
const lines = invoiceAndDateText!.trim().split('\n').map(l => l.trim()).filter(l => l);
expect(lines.length).toBeGreaterThanOrEqual(2);
```

The test assumes column 0 of the table contains BOTH the invoice number AND the date on
separate lines (newline-separated). If the cell now renders them without a `\n` separator
(e.g. a `<br>` between two `<div>` elements whose `.textContent()` concatenates without newline),
`lines.length` will be 1 instead of 2.

**Investigation steps:**
1. Open the invoice classification history page on staging and inspect the first column's DOM
2. Call `.textContent()` on the cell and log the raw string to confirm whether `\n` is present
3. Check if the column structure changed (e.g. from `<div>\n<div>` to a single `<div>`)

**Fix:**
Update the splitting logic to handle both newline and inline formats:
```typescript
// Split by newline OR by detecting the date pattern DD.MM.YYYY in the text
const text = invoiceAndDateText!.trim();
const dateMatch = text.match(/(\d{2}\.\d{2}\.\d{4})/);
const invoiceNumber = text.replace(dateMatch?.[0] ?? '', '').trim();
const dateText = dateMatch?.[0] ?? '';
```

OR — if the date is no longer in the same cell — update the column index to match the current table layout.

**Acceptance Criteria:**
- [ ] Test `should apply all four filters together` passes against staging
- [ ] No other invoice classification tests regress (run: `./scripts/run-playwright-tests.sh core`)
- [ ] The fix handles the actual cell format observed on staging (not just assumed format)
- [ ] If the table structure changed, a comment explains the column layout

---

## Functional Requirements

- FR-1: `waitForSearchResults` in `wait-helpers.ts` MUST NOT be registered after the triggering network request has already been sent and returned
- FR-2: All `waitForResponse` calls MUST be set up before the action that triggers the response, OR removed if the response is already guaranteed by a preceding UI wait
- FR-3: Dashboard tile `data-testid` selectors in tests MUST match the actual tile IDs generated by the running application on staging
- FR-4: The invoice classification table parsing logic MUST handle the actual DOM text format of column 0 on staging
- FR-5: No timeout values (Playwright `timeout`, `waitForTimeout` durations) may be increased as part of any fix

---

## Non-Goals

- Do not fix the skipped tests (they are intentionally skipped with documented reasons)
- Do not refactor the entire helper library — scope changes to the minimum needed to fix the failures
- Do not add new test data fixtures — use existing ones
- Do not change application source code unless the root cause is a genuine application bug (not a test bug)

---

## Technical Considerations

- All E2E tests run against **staging** (`https://heblo.stg.anela.cz`) — fixes must account for real network latency
- Run tests with: `./scripts/run-playwright-tests.sh <module>` — never run Playwright directly
- Authentication is handled by `navigateToCatalog`, `navigateToApp`, etc. — do not change auth helpers
- `page.waitForResponse` in Playwright only captures responses that occur AFTER the listener is registered — this is the core of the US-001/US-002 investigation

---

## Success Metrics

- Nightly run after fixes: 0 failed tests in the 3 affected files
- No new timeouts or flakiness introduced in the catalog, core/dashboard, or core/invoice-classification-history modules
- Total passing tests ≥ 294 (current total), failed = 0 for the 10 regressions

---

## Open Questions

- US-001: Is `waitForLoadingComplete` returning before the API response arrives because the catalog page uses a different loading indicator attribute than the ones in the selector? This should be confirmed during investigation.
- US-003: Was `BackgroundTaskStatusTile` intentionally removed or modified in a recent deployment? Check recent git commits touching the dashboard tile system.
- US-004: Did the invoice classification table receive a layout change that moved the date to a separate column? Check recent commits to the invoice classification frontend component.
