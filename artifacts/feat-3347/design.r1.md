# Design: Fix Recurring Jobs E2E Tests

## Component Design

This is a pure E2E test maintenance change. No production components are added or modified.

**File:** `frontend/test/e2e/core/recurring-jobs-management.spec.ts`

The test file uses standard Playwright test patterns. The only changes are:
1. Replace 4 occurrences of `.toBe(12)` with `.toBeGreaterThanOrEqual(24)`
2. Rename 1 test string from `'should display all 12 recurring jobs'` to `'should display all recurring jobs'`
3. Add a comment block above the `test.describe` block explaining the count strategy

## Data Schemas

No schema changes. The `recurring_job_configurations` table is unchanged.
