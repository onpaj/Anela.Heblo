# Specification: Fix Issued-Invoices Filter E2E Selectors

## Summary

Three E2E tests in `frontend/test/e2e/issued-invoices/filters.spec.ts` fail on the nightly run against staging. Two tests time out because their checkbox locators use `.filter({ hasText })` on `input[type="checkbox"]` elements that carry no text — the text lives in a sibling `<span>` inside the wrapping `<label>`. One test fails because its empty-state assertion path is never reached (staging data returns results for "2024"), causing a false failure signal. The fix is purely mechanical: update the two checkbox locators to reach the `<input>` through its `<label>` ancestor, and guard the invoice-ID empty-state assertion so the test passes whether or not results exist.

## Background

The nightly E2E run (ID 28147951139, 2026-06-25) reported three failures in the issued-invoices filter suite:

- **Test 3 `:70`** — invoice-ID filter, empty-state copy not visible.
- **Test 8 `:177`** — "Show Only Unsynced" checkbox, `locator.check()` times out 30 s.
- **Test 9 `:202`** — "Show Only With Errors" checkbox, `locator.check()` times out 30 s.

Source inspection of `frontend/src/pages/customer/IssuedInvoicesPage.tsx` (lines 528–548) reveals:

```tsx
<label className="flex items-center text-sm">
  <input type="checkbox" checked={showOnlyUnsynced} ... />
  <span className="ml-1 text-gray-700">Nesync</span>
</label>
<label className="flex items-center text-sm">
  <input type="checkbox" checked={showOnlyWithErrors} ... />
  <span className="ml-1 text-gray-700">Chyby</span>
</label>
```

The test code uses:
```ts
page.locator('input[type="checkbox"]').filter({ hasText: "Nesync" })
```

`input` elements are void elements — they have no inner text. Playwright's `.filter({ hasText })` matches against the element's own text content, not that of sibling elements. Therefore the locator matches zero elements and `.check()` times out after 30 s.

The invoice-ID test (test 3) uses the search term "2024", which returns results on staging. The code correctly handles this via a conditional (`if (filteredCount === 0) { ... } else { expect(firstRowText).toContain("2024"); }`), so the test should pass when results exist. The triage report's description ("empty-state not visible") is misleading — the actual failure is that staging data returns results, the else branch runs, and `firstRowText` may not contain "2024" if the invoice IDs do not include that string. The fix must make both branches of the condition robust.

## Functional Requirements

### FR-1: Fix Unsynced Checkbox Locator (Test 8, line 177)

Replace the current locator:
```ts
page.locator('input[type="checkbox"]').filter({ hasText: "Nesync" })
```
with a locator that finds the checkbox by traversing its label:
```ts
page.locator('label').filter({ hasText: 'Nesync' }).locator('input[type="checkbox"]')
```

**Acceptance criteria:**
- `unsyncedCheckbox.check()` completes without timing out on staging.
- After checking, `waitForLoadingComplete` resolves and the table renders (zero or more rows).
- Test 8 passes end-to-end in the nightly Playwright run.

### FR-2: Fix Errors Checkbox Locator (Test 9, line 202)

Replace the current locator:
```ts
page.locator('input[type="checkbox"]').filter({ hasText: "Chyby" })
```
with:
```ts
page.locator('label').filter({ hasText: 'Chyby' }).locator('input[type="checkbox"]')
```

**Acceptance criteria:**
- `errorsCheckbox.check()` completes without timing out on staging.
- After checking, `waitForLoadingComplete` resolves and the table renders (zero or more rows).
- Test 9 passes end-to-end in the nightly Playwright run.

### FR-3: Fix Invoice-ID Filter Empty-State Assertion (Test 3, line 38–67)

The test fills "2024" into `#invoiceId` and expects either empty-state text or rows containing "2024". On staging this succeeds if `firstRowText` contains "2024" (invoice ID column includes the year). The triage reports a failure, meaning either: (a) staging returned rows but no row contained "2024" in its text content, or (b) the empty-state path triggered but the element was not visible.

Verify the actual failure mode by reading the full error from run 28147951139, then apply the appropriate fix:

- **If staging returns rows**: change the `else` branch assertion to verify that the invoice ID column (the first `<td>`) of the first row contains "2024", not the full row text (which includes dates, customer names, amounts, etc. and may incidentally omit "2024" if the invoice ID is numeric-only and the string "2024" appears nowhere else).
- **If staging returns no rows and the empty-state element is not visible**: the empty-state `<p>` tag at line 694 of `IssuedInvoicesPage.tsx` renders `Žádné faktury nebyly nalezeny.` — confirm the exact text match (no trailing period discrepancy, no encoding issue) and update the locator if the copy has changed.

Assumption (noted for open questions): the most likely scenario is that the API returns results for "2024" but the full `textContent()` of the first row does not contain the string "2024" literally. The spec assumes this is the root cause.

**Acceptance criteria:**
- When the filter returns zero rows, `page.locator('p:has-text("Žádné faktury nebyly nalezeny.")')` or `page.locator('text="Žádné faktury nebyly nalezeny."')` is visible.
- When the filter returns one or more rows, at least the first row's invoice-ID cell contains "2024".
- Test 3 passes on staging regardless of whether live data returns 0 or N rows.

## Non-Functional Requirements

### NFR-1: Performance

No performance requirements. All three changes are selector updates that do not alter application code or network calls.

### NFR-2: Correctness / Regression Safety

- The three fixed tests must not mask real bugs. Specifically: the checkbox tests must assert some observable outcome after checking (zero or more rows is acceptable — the UI filtering itself is the signal that the interaction worked). They currently do this correctly and the assertion logic is unchanged.
- No other passing tests in `filters.spec.ts` may be broken by the selector changes.

### NFR-3: Maintainability

Updated selectors must be resilient to minor label text changes. Using `label.filter({ hasText })` is preferred over `nth(0)` or `nth(1)` positional selectors, so tests remain legible and tied to the UI label text.

## Data Model

Not applicable. This task touches only E2E test selectors — no backend entities, database schema, or API contracts are modified.

## API / Interface Design

Not applicable. The fix is confined to `frontend/test/e2e/issued-invoices/filters.spec.ts`. No API, component, or page code changes are required unless the empty-state copy in `IssuedInvoicesPage.tsx` has drifted from what the test expects (see FR-3).

## Implementation Plan

All changes are in a single file: `frontend/test/e2e/issued-invoices/filters.spec.ts`.

1. **Test 8 (line 179–181):** Replace locator.
   ```ts
   // Before
   const unsyncedCheckbox = page
     .locator('input[type="checkbox"]')
     .filter({ hasText: "Nesync" });
   // After
   const unsyncedCheckbox = page
     .locator('label')
     .filter({ hasText: 'Nesync' })
     .locator('input[type="checkbox"]');
   ```

2. **Test 9 (line 204–206):** Replace locator.
   ```ts
   // Before
   const errorsCheckbox = page
     .locator('input[type="checkbox"]')
     .filter({ hasText: "Chyby" });
   // After
   const errorsCheckbox = page
     .locator('label')
     .filter({ hasText: 'Chyby' })
     .locator('input[type="checkbox"]');
   ```

3. **Test 3 (line 62–66):** Tighten the `else` branch assertion.
   ```ts
   // Before
   const firstRowText = await tableRows.first().textContent();
   expect(firstRowText).toContain("2024");
   // After
   const firstCell = tableRows.first().locator('td').first();
   const firstCellText = await firstCell.textContent();
   expect(firstCellText).toContain("2024");
   ```
   This narrows the assertion to the invoice ID column only, which is guaranteed to contain the search term when results are returned.

## Dependencies

- Playwright test runner (already configured in the project).
- Staging environment with live issued-invoices data accessible via `navigateToIssuedInvoices()`.
- No new packages, libraries, or infrastructure changes required.

## Out of Scope

- Changes to `IssuedInvoicesPage.tsx` or any other application source file.
- Adding new filter test cases or expanding test coverage beyond fixing the three failing tests.
- Changing backend filter logic, API contracts, or database queries.
- Modifying other test files in the `issued-invoices/` directory.

## Open Questions

1. **Test 3 root cause confirmation**: The triage says "empty-state `text="Žádné faktury nebyly nalezeny."` not visible" but the component renders that exact string (line 694 of `IssuedInvoicesPage.tsx`). The actual Playwright error message from run 28147951139 would confirm whether the empty-state path was taken (and the copy mismatched) or whether the else-branch ran and the `firstRowText` assertion failed. The implementation plan above (tighten the `else` branch to check only the first `<td>`) is the most probable fix, but this should be confirmed against the actual error output before committing.

## Status: HAS_QUESTIONS
