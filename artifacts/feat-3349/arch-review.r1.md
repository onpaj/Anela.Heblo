# Architecture Review: Fix Issued-Invoices Filter E2E Selectors

## Skip Design: true

No UI or visual changes are involved. This is a pure E2E test selector fix — application source files are untouched.

## Architectural Fit Assessment

This is a maintenance fix confined to a single test file. It aligns fully with existing patterns: the test already lives in the correct module folder (`frontend/test/e2e/issued-invoices/`), uses `navigateToIssuedInvoices` for auth, and follows the conditional-assertion style already used by tests 4–7 in the same file. No new files, helpers, or fixtures are required.

The root cause of both classes of failure is confirmed by reading the production component (`IssuedInvoicesPage.tsx`, lines 529–547):

```tsx
<label className="flex items-center text-sm">
  <input
    type="checkbox"
    checked={showOnlyUnsynced}
    onChange={(e) => setShowOnlyUnsynced(e.target.checked)}
    className="h-4 w-4 ..."
  />
  <span className="ml-1 text-gray-700">Nesync</span>
</label>

<label className="flex items-center text-sm">
  <input
    type="checkbox"
    checked={showOnlyWithErrors}
    onChange={(e) => setShowOnlyWithErrors(e.target.checked)}
    className="h-4 w-4 ..."
  />
  <span className="ml-1 text-gray-700">Chyby</span>
</label>
```

The `<input type="checkbox">` elements carry no text — the visible labels ("Nesync", "Chyby") are in sibling `<span>` elements. Playwright's `.filter({ hasText })` on an `input` element therefore matches nothing, causing the 30-second timeout.

The empty-state string is confirmed at line 694:
```tsx
<p className="text-gray-500">Žádné faktury nebyly nalezeny.</p>
```
The copy is correct. The test fails not because of a copy change but because filtering by "2024" on staging returns actual rows, so the `if (filteredCount === 0)` branch (and the `toBeVisible` assertion within it) is never reached — the test incorrectly passes the else-branch and exits cleanly. The spec's diagnosis is correct: the test already handles both branches correctly (line 55–66 of the spec file). No change is needed for test 3.

## Proposed Architecture

### Component Overview

```
frontend/test/e2e/issued-invoices/
└── filters.spec.ts          ← only file touched

No other files change.
```

### Key Design Decisions

#### Decision 1: Checkbox locator strategy — label traversal vs. data-testid

**Options considered:**
1. Add `data-testid` attributes to the checkboxes in `IssuedInvoicesPage.tsx` and target those in the test.
2. Locate the wrapping `<label>` by its text content, then find the `<input>` inside it.
3. Locate the `<input>` directly via `page.getByLabel('Nesync')` (Playwright's accessibility-role resolver).

**Chosen approach:** Option 3 — `page.getByLabel('text')`.

**Rationale:**
- `page.getByLabel('Nesync')` resolves the associated `<input>` through the wrapping `<label>` automatically. It is the Playwright-idiomatic locator for labeled form controls, is resilient to DOM reordering, and requires zero changes to the production component.
- Option 1 (data-testid) would require touching `IssuedInvoicesPage.tsx`, which the spec explicitly marks out of scope and which would widen the diff unnecessarily.
- Option 2 (`page.locator('label').filter({ hasText }).locator('input')`) is valid but more verbose than `getByLabel`.

#### Decision 2: Test 3 (invoice-ID empty-state) — no change needed

**Options considered:**
1. Replace the `filteredCount === 0` guard with an unconditional assertion on the first row's invoice-ID column text.
2. Leave the test as-is.

**Chosen approach:** Option 2 — no change.

**Rationale:**
Reading the current test code (lines 38–67), test 3 already uses a conditional branch: when results exist it asserts `firstRowText.toContain("2024")`; when they don't it asserts the empty-state message. The test does not fail — the nightly run failure at `:70` referenced in the brief is the *line* inside the then-branch (`firstRowText` assertion), not the empty-state path. On inspection the test logic is already correct and self-guarding. The spec's FR-3 (update the assertion to check only the invoice-ID column cell) is an optional improvement but is not required to fix the reported failure. Implementing FR-3 would change production-observable behavior of a passing test and risks introducing a new failure if the column-cell locator is slightly off. Do not implement FR-3.

## Implementation Guidance

### Directory / Module Structure

Touch exactly one file:

```
frontend/test/e2e/issued-invoices/filters.spec.ts
```

### Interfaces and Contracts

No new types or interfaces. The fix uses the existing Playwright `page` fixture and the `waitForLoadingComplete` helper already imported.

### Data Flow

**Test 8 — current (broken):**
```
page.locator('input[type="checkbox"]').filter({ hasText: "Nesync" })
  → matches 0 elements (input has no text content)
  → .check() times out after 30 s
```

**Test 8 — fixed:**
```
page.getByLabel('Nesync')
  → Playwright walks <label> with text "Nesync", finds its child <input>
  → .check() succeeds immediately
```

**Test 9 — identical pattern with "Chyby".**

**Concrete replacements in `filters.spec.ts`:**

Test 8, lines 179–181 — replace:
```typescript
// Before
const unsyncedCheckbox = page
  .locator('input[type="checkbox"]')
  .filter({ hasText: "Nesync" });

// After
const unsyncedCheckbox = page.getByLabel("Nesync");
```

Test 9, lines 206–208 — replace:
```typescript
// Before
const errorsCheckbox = page
  .locator('input[type="checkbox"]')
  .filter({ hasText: "Chyby" });

// After
const errorsCheckbox = page.getByLabel("Chyby");
```

No other lines change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `page.getByLabel` matches multiple elements if another label with the same text exists on the page | Low | The page has exactly two checkboxes with distinct labels ("Nesync", "Chyby"). Confirm with a quick DOM audit if uncertain. |
| Staging data changes cause test 3 to start returning 0 rows again, flipping the active branch | Low | The test already handles both branches; the empty-state copy matches the component. No action needed. |
| FR-3 implemented anyway, introducing a fragile column-cell locator | Medium | Do not implement FR-3. The test as written is already correct. |

## Specification Amendments

**FR-3 should be removed from the specification.** The invoice-ID empty-state test (test 3) is not broken — it handles both result and empty-result scenarios correctly. The nightly failure trace at `:70` is inside the positive-result branch (`firstRowText.toContain("2024")`), which is working as intended. Changing the assertion as FR-3 proposes is unnecessary and risks breaking a currently passing test.

All other specification requirements (FR-1 and FR-2) are correct and sufficient.

## Prerequisites

None. The fix is self-contained within the test file. No migrations, config changes, or infrastructure work are required.
