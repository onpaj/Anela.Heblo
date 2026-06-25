# Implementation: fix-auth-helper-url

## What was implemented
Single-character string fix in the `navigateToStockOperations` helper: the `page.goto` call was navigating to `/stock-operations` (the wrong route) and has been corrected to `/stock-up-operations` (the actual frontend route).

## Files created/modified
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — changed `/stock-operations` to `/stock-up-operations` in the `page.goto` template literal at line 270.

## Tests
N/A — this is a test file fix; no additional tests needed.

## How to verify
Run the E2E tests that exercise stock operations navigation (e.g. any spec that calls `navigateToStockOperations`). The browser should land on the `/stock-up-operations` page instead of a 404/redirect.

## Notes
No deviations from the task spec. The `baseUrl` env-var chain was already correct and was not touched.

## PR Summary

### Changes
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — corrected navigateToStockOperations URL from `/stock-operations` to `/stock-up-operations`

## Status
DONE
