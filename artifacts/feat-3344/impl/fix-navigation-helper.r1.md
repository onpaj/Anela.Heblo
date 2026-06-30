# Implementation: fix-navigation-helper

## What was implemented
Added readiness waits to both navigation paths in `navigateToMarketingCalendar`. After sidebar navigation and after direct navigation, the function now waits for `h1 "Marketingový kalendář"` to be visible before returning. Also added a clarifying comment that the sidebar `text="Kalendář"` selector targets the `<a>` link, not a view-toggle button.

## Files created/modified
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — added two `waitFor` calls and one clarifying comment in `navigateToMarketingCalendar`

## Tests
No new test files — this is a test-helper change. All marketing specs that call `navigateToMarketingCalendar` are the integration verification.

## How to verify
Run `npx playwright test frontend/test/e2e/marketing/ --project=chromium` against staging. Tests should no longer time out at 35 seconds waiting for non-existent elements.

## Notes
- The sidebar `text="Kalendář"` selector is intentionally unchanged — it matches the sidebar `<a href="/marketing/calendar">` link correctly
- Used `waitFor({ timeout: 15000 })` to allow for slow staging performance
- No new imports required; `waitFor` is a native Playwright locator method

## PR Summary
Fixed `navigateToMarketingCalendar` to wait for `h1 "Marketingový kalendář"` before returning, eliminating the root cause of 35-second E2E timeouts.

### Changes
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — added readiness waits and clarifying comment in `navigateToMarketingCalendar`

## Status
DONE
