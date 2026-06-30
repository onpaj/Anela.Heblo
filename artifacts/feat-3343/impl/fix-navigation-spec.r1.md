# Implementation: fix-navigation-spec

## What was implemented

Two URL corrections in the stock-operations E2E navigation spec:

1. Line 13: URL assertion updated from `/stock-operations` to `/stock-up-operations` to match the actual frontend route.
2. Lines 95–96: Direct `page.goto` URL updated from `/stock-operations` to `/stock-up-operations`, and the `baseUrl` fallback chain extended to check `PLAYWRIGHT_FRONTEND_URL` first (before `PLAYWRIGHT_BASE_URL`), aligning it with the helper's env-var resolution order.

## Files created/modified

- `frontend/test/e2e/stock-operations/navigation.spec.ts` — fixed URL assertion on line 13 and direct goto URL + baseUrl fallback chain on lines 95–96

## Tests

N/A — this is a test file fix.

## How to verify

Run the navigation spec against staging:

```bash
./scripts/run-playwright-tests.sh --grep "should navigate to page via direct URL"
./scripts/run-playwright-tests.sh --grep "should display error state on API failure"
```

Both tests should reach the correct `/stock-up-operations` route without a 404 redirect.

## Notes

No deviations from the task spec. The two surrounding lines were unchanged; only the route path and the `baseUrl` variable initializer were touched.

## PR Summary

### Changes

- `frontend/test/e2e/stock-operations/navigation.spec.ts` — corrected `/stock-operations` → `/stock-up-operations` in the URL assertion (line 13) and in the direct `page.goto` call (line 96); added `PLAYWRIGHT_FRONTEND_URL` as the first fallback in the `baseUrl` resolution chain (line 95)

## Status

DONE
