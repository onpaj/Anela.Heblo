# Implementation: fix-recurring-jobs-test-assertions

## What was implemented

Replaced four brittle `toBe(12)` count assertions with `toBeGreaterThanOrEqual(24)` in `frontend/test/e2e/core/recurring-jobs-management.spec.ts`. Renamed the test from `'should display all 12 recurring jobs'` to `'should display all recurring jobs'`. Added a 4-line comment block above `test.describe` explaining why `>= 24` is used and how to update the minimum in the future.

## Files created/modified

- `frontend/test/e2e/core/recurring-jobs-management.spec.ts` — updated 4 count assertions, renamed 1 test, added comment block

## Tests

The modified file IS the E2E test file. No new test files were created. The change updates existing assertions to be resilient to job count growth.

## How to verify

Run against staging:
```bash
./scripts/run-playwright-tests.sh core/recurring-jobs-management
```

All 4 previously-failing tests should now pass:
- `should display all recurring jobs` (was: `should display all 12 recurring jobs`)
- `should refresh jobs list when clicking refresh button`
- `should have proper accessibility attributes on toggle buttons`
- `should have proper accessibility attributes on Run Now buttons`

## Notes

- Root cause: 12 new `IRecurringJob` implementations were added since the tests were written, bringing the total to 24. No duplication. No bug.
- `FlexiAnalyticsSyncJob` is only registered when `AnalyticsDatabase:ConnectionString` is set — staging has this configured, giving exactly 24 jobs.
- The `expectedJobs` array at lines ~223–235 (asserting the original 12 jobs by name) was intentionally left unchanged — it correctly verifies the original jobs still exist.

## PR Summary

Replaced brittle hard-coded count assertions (`toBe(12)`) in the recurring-jobs E2E suite with resilient minimum-count assertions (`toBeGreaterThanOrEqual(24)`). Root-cause investigation confirmed that staging now has 24 `IRecurringJob` implementations — the count grew legitimately from 12 to 24, there is no rendering duplication, and the `bug` label should be removed.

### Changes
- `frontend/test/e2e/core/recurring-jobs-management.spec.ts` — 4 assertions updated, 1 test renamed, explanatory comment added

## Status
DONE
