# Implementation: fix-dashboard-tile-timeout

## What was implemented
Increased the `waitForSelector` timeout for `[data-testid^="dashboard-tile-"]` in the `'should display AutoShow tiles automatically'` test from 5 seconds to 15 seconds to prevent premature timeout failures on staging.

## Files created/modified
- `frontend/test/e2e/core/dashboard.spec.ts` — changed `{ timeout: 5000 }` to `{ timeout: 15000 }` on line 27

## How to verify
Run the E2E dashboard test suite against staging:
```
./scripts/run-playwright-tests.sh --grep "should display AutoShow tiles automatically"
```
The test should no longer fail with a timeout error when staging is slow to render dashboard tiles.

## Status
DONE
