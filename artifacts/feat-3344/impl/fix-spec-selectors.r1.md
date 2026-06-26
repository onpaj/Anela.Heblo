# Implementation: fix-spec-selectors

## What was implemented
Replaced all stale `button … hasText 'Kalendář'` view-toggle selectors with `button … hasText '5 týdnů'` across four marketing spec files. Added a "14 dní" button assertion to `loading.spec.ts`. Fixed the mobile-agenda `h1` assertion from "Kalendář" to "Marketingový kalendář". Confirmed `create-record.spec.ts` requires no changes.

## Files created/modified
- `frontend/test/e2e/marketing/loading.spec.ts` — replaced "Kalendář" toggle selector with "5 týdnů"; added "14 dní" visibility assertion
- `frontend/test/e2e/marketing/calendar-view.spec.ts` — replaced "Kalendář" toggle selector in `beforeEach` with "5 týdnů"
- `frontend/test/e2e/marketing/grid-view.spec.ts` — replaced "Kalendář" toggle selector in deactivation test with "5 týdnů"
- `frontend/test/e2e/marketing/mobile-agenda.spec.ts` — replaced `h1 hasText 'Kalendář'` with `h1 hasText 'Marketingový kalendář'`

## Tests
No new test files needed — these ARE the test files being fixed.

## How to verify
Run `npx playwright test frontend/test/e2e/marketing/ --project=chromium` against staging. All 5 spec files should pass.

Confirm zero stale selectors: `grep -r "hasText: 'Kalendář'" frontend/test/e2e/marketing/` should return no results.

## Notes
- `create-record.spec.ts` was verified to contain no stale "Kalendář" toggle-button selectors and was not modified
- All changes are pure string-literal replacements; no TypeScript types change
- "Seznam" selectors and sidebar navigation in the helper are unchanged

## PR Summary
Updated marketing E2E spec files to use the new view-toggle button labels ("5 týdnů", "14 dní") introduced in the calendar UI redesign, replacing stale "Kalendář" button selectors that caused 35-second timeouts.

### Changes
- `frontend/test/e2e/marketing/loading.spec.ts` — replaced "Kalendář" toggle with "5 týdnů"; added "14 dní" assertion
- `frontend/test/e2e/marketing/calendar-view.spec.ts` — replaced "Kalendář" toggle in beforeEach with "5 týdnů"
- `frontend/test/e2e/marketing/grid-view.spec.ts` — replaced "Kalendář" toggle in deactivation test with "5 týdnů"
- `frontend/test/e2e/marketing/mobile-agenda.spec.ts` — fixed h1 assertion to "Marketingový kalendář"

## Status
DONE
