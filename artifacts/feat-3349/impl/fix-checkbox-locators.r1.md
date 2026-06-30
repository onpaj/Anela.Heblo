# Implementation: fix-checkbox-locators

## What was implemented
Replaced two broken `.filter({ hasText })` checkbox locators in the issued-invoices E2E filter tests with `page.getByLabel()` calls. The old approach used `.locator('input[type="checkbox"]').filter({ hasText: ... })`, which does not work correctly for label-wrapped inputs because Playwright's `filter({ hasText })` checks the element's own text content, not its associated label. `getByLabel()` correctly resolves the input via the wrapping or associated `<label>` element.

## Files created/modified
- `frontend/test/e2e/issued-invoices/filters.spec.ts` — replaced checkbox locators in tests 8 and 9

## Tests
N/A — this IS the test fix

## How to verify
1. Run the E2E suite against staging: `./scripts/run-playwright-tests.sh`
2. Confirm tests 8 ("Show Only Unsynced checkbox") and 9 ("Show Only With Errors checkbox") locate and interact with the checkboxes without a locator error.

## Notes
- `npm run lint` targets `src/` only (`eslint src --ext .ts,.tsx`) so E2E test files under `test/` are outside its scope — no lint impact from these changes.
- The lint command itself cannot run from the worktree because `node_modules/` are not installed there; the main repo has pre-existing lint errors unrelated to this change.
- No other lines in the file were modified beyond the two locator replacements.

## PR Summary
Fix checkbox locators in issued-invoices E2E filter tests (tests 8 and 9). The previous `.locator('input[type="checkbox"]').filter({ hasText })` pattern does not work for label-wrapped inputs; replaced with `page.getByLabel()` which correctly resolves inputs through their associated labels.

### Changes
- `frontend/test/e2e/issued-invoices/filters.spec.ts` — replaced broken checkbox selectors in tests 8 and 9 with `page.getByLabel("Nesync")` and `page.getByLabel("Chyby")` respectively

## Status
DONE
