# Architecture Review: Update Marketing Calendar E2E Selectors

## Skip Design: true

## Architectural Fit Assessment

This change is entirely contained within the E2E test layer. No production source code, API contracts, or data models are affected. The marketing E2E module already follows the established pattern: a shared navigation helper in `frontend/test/e2e/helpers/e2e-auth-helper.ts` plus per-concern spec files under `frontend/test/e2e/marketing/`. The fix is a straight selector update — it does not introduce new patterns, new helpers, or new test infrastructure. Alignment with the existing architecture is complete.

The only integration point is `navigateToMarketingCalendar` in the shared helper, which is called by every marketing spec. Adding a readiness wait there fixes the root cause for all callers simultaneously, which is the correct leverage point.

## Proposed Architecture

### Component Overview

```
frontend/test/e2e/
├── helpers/
│   └── e2e-auth-helper.ts          ← FR-1: add readiness wait after navigation
└── marketing/
    ├── loading.spec.ts             ← FR-2: replace 'Kalendář' toggle selectors
    ├── calendar-view.spec.ts       ← FR-3: replace beforeEach toggle selector
    ├── grid-view.spec.ts           ← FR-4: replace calendarToggle selector
    ├── mobile-agenda.spec.ts       ← FR-5: replace h1 hasText filter
    └── create-record.spec.ts       ← FR-6: no changes (confirmed clean)
```

All changes are leaf-level edits. No new files. No new helpers. No changes to test infrastructure (`playwright.config.ts`, `wait-helpers.ts`, fixtures).

### Key Design Decisions

#### Decision 1: Readiness wait strategy in navigateToMarketingCalendar
**Options considered:**
- Wait for `h1` containing "Marketingový kalendář"
- Wait for `button` containing "5 týdnů"
- Wait for either via `page.locator(...).or(page.locator(...))`

**Chosen approach:** Wait for `page.locator('h1', { hasText: 'Marketingový kalendář' })` to be visible. Use `.or()` with `page.locator('button', { hasText: '5 týdnů' })` as a secondary condition only if the `h1` proves unstable on slow loads in practice — start with just the `h1`.

**Rationale:** The `h1` is present on the page regardless of which view toggle is active and regardless of viewport width. It is the most stable landmark. The spec itself (FR-1) lists it as the primary option. Toggle buttons are view-state-dependent; the heading is always present after a successful page load. Using a single, stable selector reduces brittleness.

#### Decision 2: Scope of the "5 týdnů" label — do not introduce page objects or constants
**Options considered:**
- Inline string literals in each spec (current pattern)
- Extract toggle labels into a shared constants file

**Chosen approach:** Inline string literals, matching the existing pattern throughout the marketing spec files.

**Rationale:** The codebase uses inline selectors consistently. Introducing a constants file for three string values would be over-engineering for a surgical fix. If the UI redesigns again, the same grep-and-replace approach applies. Keep the change minimal and consistent with the existing style.

#### Decision 3: mobile-agenda.spec.ts h1 assertion correction
**Options considered:**
- Change `hasText: 'Kalendář'` to `hasText: 'Marketingový kalendář'`
- Use a partial-match regex `/Kalendář/` which would still match

**Chosen approach:** Use the exact string `'Marketingový kalendář'` to match the actual `h1` content.

**Rationale:** Exact match is already used in the resize-to-desktop assertion in the same file. Consistency within the file and precision over leniency.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. All edits are in-place to existing files.

### Interfaces and Contracts

The selector strings that change:

| Old selector text | New selector text | Location |
|---|---|---|
| `button … hasText 'Kalendář'` (toggle) | `button … hasText '5 týdnů'` | `loading.spec.ts`, `calendar-view.spec.ts`, `grid-view.spec.ts` |
| (none — no readiness wait) | `h1 … hasText 'Marketingový kalendář'` | `e2e-auth-helper.ts` |
| `h1 … hasText: 'Kalendář'` | `h1 … hasText: 'Marketingový kalendář'` | `mobile-agenda.spec.ts` |

The "14 dní" button assertion is additive (new assertion in `loading.spec.ts`). The "Seznam" button and sidebar `text="Kalendář"` link are unchanged throughout.

### Data Flow

```
navigateToMarketingCalendar(page)
  └─ navigateToApp(page)                     [unchanged]
  └─ waitForLoadingComplete(page)            [unchanged]
  └─ sidebar click 'Marketing' → 'Kalendář' [unchanged — sidebar <a> link]
       OR direct navigate /marketing/calendar [unchanged]
  └─ [NEW] page.waitForSelector(            [FR-1 addition]
         h1 "Marketingový kalendář"
         OR button "5 týdnů",
         timeout: default Playwright timeout
     )
  └─ returns                                [caller test proceeds immediately]
```

Each spec's `beforeEach` then calls this helper, after which selectors reference the current UI labels.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| "5 týdnů" label changes again in a future redesign | Low | Tests will fail fast (seconds, not 35s) with a clear "element not found" message — no special mitigation needed, that is the desired behaviour |
| `h1` loads but toggle buttons are still mounting (race between heading and React hydration of toolbar) | Low | The existing `waitForLoadingComplete` call before navigation already handles the loading spinner; the `h1` appearing implies the component tree is rendered |
| `mobile-agenda.spec.ts` h1 exact-match fails if `h1` text includes surrounding whitespace | Low | Playwright `hasText` does substring matching by default; "Marketingový kalendář" will match even with surrounding whitespace |
| `create-record.spec.ts` silently depends on `navigateToMarketingCalendar` stability | Low | FR-6 confirms no selector changes needed; FR-1 fix improves reliability for this file as a side effect |

## Specification Amendments

None required. The spec is complete and unambiguous. One clarification worth encoding in implementation comments:

- In `e2e-auth-helper.ts`, annotate that the sidebar step `text="Kalendář"` targets the `<a>` sidebar link (not the view-toggle button) so future maintainers do not incorrectly change it.

## Prerequisites

- Staging environment must be running the redesigned marketing calendar UI (the one with "5 týdnů", "14 dní", "Seznam" toggles). This is confirmed deployed per the nightly run referenced in the spec.
- No migrations, config changes, or infrastructure work required.
- No new npm packages required.
- Run `./scripts/run-playwright-tests.sh` targeting the `marketing/` module against staging to confirm all 5 spec files pass after changes.
