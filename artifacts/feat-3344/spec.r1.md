# Specification: Update Marketing Calendar E2E Selectors

## Summary
Marketing E2E tests are failing because the view-toggle UI was redesigned. Tests wait for a `button` with text "Kalendář" which no longer exists. This change updates all affected test files and the navigation helper to use the new toggle labels ("5 týdnů", "14 dní", "Seznam").

## Background
The marketing calendar page was redesigned. The old single "Kalendář" toggle button is gone. The page now shows three view-toggle buttons: "5 týdnů" (5-week calendar grid), "14 dní" (14-day view), and "Seznam" (list/grid view). An additional "Nová akce" button is present. The sidebar link remains `<a href="/marketing/calendar">` and the page heading remains "Marketingový kalendář". Nightly E2E run 28147951139 shows ~15 marketing test failures from 35-second timeouts.

## Functional Requirements

### FR-1: Update navigateToMarketingCalendar readiness wait
In `frontend/test/e2e/helpers/e2e-auth-helper.ts`, the `navigateToMarketingCalendar` function currently has no explicit readiness wait after navigation. After the direct-navigation fallback and after the sidebar navigation, add a wait for either the `h1` containing "Marketingový kalendář" or one of the new toggle buttons ("5 týdnů") to be visible, confirming the calendar page has loaded.

The sidebar navigation step that clicks `text="Kalendář"` refers to the sidebar anchor link (not a toggle button), so that part is correct — `text="Kalendář"` matches the `<a>` link text in the sidebar. No change needed there.

**Acceptance criteria:**
- After `navigateToMarketingCalendar` resolves, the page heading "Marketingový kalendář" is visible
- No 35-second timeout occurs waiting for a non-existent "Kalendář" toggle button

### FR-2: Fix loading.spec.ts view-toggle assertions
In `frontend/test/e2e/marketing/loading.spec.ts`:
- Test "should display page heading and toolbar controls": replace `button … hasText 'Kalendář'` with `button … hasText '5 týdnů'` and add `button … hasText '14 dní'`
- Test "should load calendar view by default": replace `button … hasText 'Kalendář'` (used for active-state check) with `button … hasText '5 týdnů'`; replace `button … hasText 'Seznam'` list toggle check remains unchanged

**Acceptance criteria:**
- `loading.spec.ts` assertions reference "5 týdnů" and "14 dní" instead of "Kalendář" for the view toggle buttons
- The "Seznam" button assertion is retained as-is

### FR-3: Fix calendar-view.spec.ts beforeEach toggle
In `frontend/test/e2e/marketing/calendar-view.spec.ts`:
- `beforeEach` clicks `button … hasText 'Kalendář'` to activate calendar view — replace with `button … hasText '5 týdnů'`
- The `expect(calendarToggle).toHaveClass(/bg-indigo-600/)` assertion stays; only the selector text changes

**Acceptance criteria:**
- `beforeEach` in `calendar-view.spec.ts` activates calendar view by clicking "5 týdnů"
- Active-state assertion checks "5 týdnů" button has `bg-indigo-600` class

### FR-4: Fix grid-view.spec.ts deactivated-toggle assertion
In `frontend/test/e2e/marketing/grid-view.spec.ts`:
- Test "should deactivate calendar toggle when switching to grid view": `calendarToggle` selector uses `button … hasText 'Kalendář'` — replace with `button … hasText '5 týdnů'`
- The "Seznam" toggle selector and click in `beforeEach` are already correct

**Acceptance criteria:**
- `grid-view.spec.ts` checks "5 týdnů" button (not "Kalendář") for deactivated state

### FR-5: Fix mobile-agenda.spec.ts heading assertion
In `frontend/test/e2e/marketing/mobile-agenda.spec.ts`:
- Test "renders the mobile agenda view…": `h1` filter uses `hasText: 'Kalendář'` — replace with `hasText: 'Marketingový kalendář'`
- The check `not.toBeVisible()` for `button … hasText '5 týdnů'` is already using the new label and is correct

**Acceptance criteria:**
- `mobile-agenda.spec.ts` asserts `h1` contains "Marketingový kalendář" (not just "Kalendář")

### FR-6: Verify create-record.spec.ts needs no changes
In `frontend/test/e2e/marketing/create-record.spec.ts`, no `button … hasText 'Kalendář'` selectors exist. The file uses "Seznam", "Nová akce", "Vytvořit", "Zrušit", "Smazat", "Uložit" — all correct. No changes needed.

**Acceptance criteria:**
- `create-record.spec.ts` is confirmed unchanged and passes after FR-1 fix

## Non-Functional Requirements

### NFR-1: Performance
Tests must not time out at 35 seconds. With correct selectors, assertions should resolve within the default Playwright timeout (typically 5–10 seconds).

### NFR-2: Test reliability
Selectors must be stable: use the button text labels ("5 týdnů", "14 dní", "Seznam") that are rendered in the current UI. Avoid brittle CSS-class-only selectors.

## Data Model
Not applicable — this is a test-only change with no production data model impact.

## API / Interface Design
Not applicable — no API changes. The marketing calendar API endpoints remain unchanged.

## Dependencies
- Playwright test framework (already installed)
- Staging environment with the redesigned marketing calendar UI deployed

## Out of Scope
- Changes to production source code (React components, backend)
- Leaflet-generator failures (tracked separately)
- Any other E2E test files outside the marketing module

## Open Questions
None.

## Status: COMPLETE
