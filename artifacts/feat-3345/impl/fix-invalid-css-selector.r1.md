# Implementation: fix-invalid-css-selector

## What was implemented
Replaced an invalid comma-separated mixed CSS/text selector in `loading.spec.ts` line 14 with the confirmed working `text="Kalendář"` Playwright text selector pattern, matching the pattern used in `navigateToMarketingCalendar()` helper.

## Files created/modified
- `frontend/test/e2e/marketing/loading.spec.ts` — replaced `page.locator('a[href="/marketing/calendar"], text="Kalendář"')` with `page.locator('text="Kalendář"')`

## How to verify
Run the E2E suite for the marketing module:
```
./scripts/run-playwright-tests.sh --grep "Marketing Calendar"
```
The test "should navigate to marketing calendar via sidebar" should pass without a selector parse error.

## Notes
No deviations. The fix is a single-line change. The `text="Kalendář"` pattern is already used at line 424 of `frontend/test/e2e/helpers/e2e-auth-helper.ts` (`navigateToMarketingCalendar`), confirming it is the correct selector for this sidebar link.

## PR Summary
Fixes a Playwright selector syntax error in the marketing calendar loading spec. The original selector `'a[href="/marketing/calendar"], text="Kalendář"'` used an invalid comma-separated mix of CSS attribute and Playwright text selectors; Playwright does not support this syntax. Replaced with `'text="Kalendář"'`, which is the pattern already proven to work in the auth helper.

### Changes
- `frontend/test/e2e/marketing/loading.spec.ts` — replace invalid mixed CSS/text selector with valid `text="Kalendář"` Playwright selector

## Status
DONE
