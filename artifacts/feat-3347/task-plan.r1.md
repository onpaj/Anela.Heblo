# Task Plan: Fix Recurring Jobs E2E Tests

## Overview

Single task: update 4 test assertions and 1 test name in `frontend/test/e2e/core/recurring-jobs-management.spec.ts` to replace brittle hard-coded counts with resilient minimum-count assertions.

---

### task: fix-recurring-jobs-test-assertions

## Goal

Update `frontend/test/e2e/core/recurring-jobs-management.spec.ts` to replace all four hard-coded `toBe(12)` count assertions with `toBeGreaterThanOrEqual(24)`, rename the test that references "12 recurring jobs", and add a comment explaining the count strategy.

## Context

Root cause confirmed: the staging environment now has 24 registered `IRecurringJob` implementations (12 original + 12 added since the tests were first written). There is no rendering duplication. The `recurring_job_configurations` table on staging has 24 rows, one per job. The UI renders one `<tr>` per row.

The `bug` label on the GitHub issue should be removed because this is not a bug — the count grew legitimately.

This is a pure E2E test file change. No backend or frontend production code is modified.

## Files to modify

- `frontend/test/e2e/core/recurring-jobs-management.spec.ts`

## Implementation steps

1. **Add comment block** immediately before the `test.describe('Recurring Jobs Management'` line (approximately line 4):
   ```typescript
   // NOTE: The recurring jobs count grows as new IRecurringJob implementations are added.
   // As of 2026-06-25, staging has 24 jobs (12 original + 12 added since initial test authoring).
   // Assertions use toBeGreaterThanOrEqual(24) so tests survive future additions without modification.
   // To update the minimum, check: SELECT COUNT(*) FROM recurring_job_configurations on staging.
   ```

2. **Rename test** at approximately line 37:
   ```typescript
   // FROM:
   test('should display all 12 recurring jobs', async ({ page }) => {
   // TO:
   test('should display all recurring jobs', async ({ page }) => {
   ```

3. **Update line ~46** (row count assertion inside the renamed test):
   ```typescript
   // FROM:
   expect(rowCount).toBe(12);
   // TO:
   expect(rowCount).toBeGreaterThanOrEqual(24);
   ```

4. **Update line ~215** (row count assertion in `should refresh jobs list when clicking refresh button`):
   ```typescript
   // FROM:
   expect(rowCount).toBe(12);
   // TO:
   expect(rowCount).toBeGreaterThanOrEqual(24);
   ```

5. **Update line ~296** (toggle button count in `should have proper accessibility attributes on toggle buttons`):
   ```typescript
   // FROM:
   expect(buttonCount).toBe(12);
   // TO:
   expect(buttonCount).toBeGreaterThanOrEqual(24);
   ```

6. **Update line ~693** (Run Now button count in `should have proper accessibility attributes on Run Now buttons`):
   ```typescript
   // FROM:
   expect(buttonCount).toBe(12);
   // TO:
   expect(buttonCount).toBeGreaterThanOrEqual(24);
   ```

7. Commit all changes to the feature branch.

## Acceptance criteria

- All four count assertions use `toBeGreaterThanOrEqual(24)` instead of `toBe(12)`.
- Test name no longer contains "12".
- Comment block present above `test.describe`.
- `npm run build` and `npm run lint` pass in the frontend directory.
- No other tests in the file are modified.
