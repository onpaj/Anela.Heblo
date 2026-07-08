# Code Review: Batch Planning Error Handling E2E Navigation Fix

## Summary
The implementation correctly addresses all five functional requirements from the specification. Navigation selectors have been replaced with role-based locators that target the correct sidebar link ("Plánování dávek"), heading assertions have been made exact and applied to both tests, and all load-wait strategies have been updated from `networkidle` to `domcontentloaded`. All behavioral assertions and fixture usage are preserved. The fix copies proven patterns from the reference `batch-planning-workflow.spec.ts` as instructed.

## Review Result: PASS

### task: fix-batch-planning-error-handling-e2e-navigation
**Status:** PASS

## Detailed Findings

**FR-1 (Navigation selector)** — Correct in both tests:
- Test 1 (line 33 diff): Changed from `text=/Plánovač|Kalkulačka dávek/i` to `getByRole('link', { name: /plánování dávek/i })`
- Test 2 (line 77 diff): Same selector change applied
- Critical improvement: The old regex never matched "Plánování dávek" (note spelling difference from "Plánovač"), but did match "Kalkulačka dávek" (wrong page). New selector matches only the correct link and explicitly does not match the calculator link.

**FR-2 (Heading assertion)** — Correctly applied to both tests:
- Test 1 (line 50 diff): Replaced permissive `h1, h2` filter with exact-match `h1` filter on `/plánovač výrobních dávek/i`
- Test 2 (lines 89–93 diff): Added the missing page-load guard that was entirely absent before this change. Added exactly where required: after `waitForLoadState` and timeout, before combobox interaction.
- Verification: New assertion correctly rejects "Kalkulačka dávek" pages, so wrong-page navigation fails loudly at this assertion rather than silently continuing to a wrong-combobox timeout.

**FR-4 (Load-wait strategy)** — All occurrences replaced:
- Diff shows 7 replacements of `networkidle` → `domcontentloaded`:
  - `beforeEach` (line 23)
  - Test 1: After navigation (line 41), after combobox selection (line 58), after recalculate button (line 67)
  - Test 2: Before navigation (line 85), after combobox selection (line 102), after recalculate (line 111)
- Matches the staging telemetry pattern described in the spec.

**FR-3/FR-5 (Preserve all behavior)** — Verified:
- Combobox interaction code for product selection unchanged
- `TestCatalogItems.hedvabnyPan` / `MAS001001M` fixture usage preserved
- Missing-data `throw` guards unchanged
- Test 1 checkbox/quantity/`Přepočítat` button flow preserved
- Test 2 no-error-toaster assertion preserved
- `navigateToApp(page)` authentication preserved (correct per CLAUDE.md rule)

**FR-6 (No helper extraction)** — Compliant:
- No new utility functions or shared navigation helpers extracted
- Explicit fallback to `page.goto('/manufacturing/batch-planning')` kept, which is acceptable per spec guidance

**Testing & Verification:**
- Implementation correctly notes that E2E test files fall outside `npm run lint`'s `src/**` scope; the standard lint command is unaffected.
- Direct ESLint check reports 4 `testing-library/prefer-screen-queries` false positives; implementation confirmed these also appear in the reference file `batch-planning-workflow.spec.ts` (which the task explicitly instructs us to copy patterns from). Not a regression.
- Pre-existing TypeScript error at line 231 (`hasClass` not valid) confirmed unrelated to this change and on a visual-indicator check marked non-critical in the spec.
- Live E2E run against staging correctly deferred to the pipeline verification step as acknowledged in the task instructions.

## Overall Notes
The implementation demonstrates careful adherence to the specification:
- Patterns copied from the proven reference file are applied correctly
- The critical selector fix (from text-based to role-based) eliminates the silent mis-navigation root cause
- The heading assertion addition to test 2 closes the safety gap where wrong-page navigation could go undetected
- All 7 wait-strategy changes are accounted for and correct
- No over-reaching changes; scope is surgical and matches the spec exactly

All acceptance criteria are satisfied. Ready for pipeline E2E verification against staging.
