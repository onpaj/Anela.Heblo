## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/test/e2e/helpers/e2e-auth-helper.ts:432` — The `h1` readiness wait in the UI-navigation path fires after `waitForLoadingComplete`, but the direct-navigation path at line 445 also duplicates that same wait. Both are correct, but if the heading wait logic ever needs updating it must be changed in two places. Consider extracting the `page.locator('h1').filter({ hasText: 'Marketingový kalendář' }).waitFor({ timeout: 15000 })` call into a small named helper (e.g., `waitForMarketingCalendarHeading`) and calling it from both branches.
