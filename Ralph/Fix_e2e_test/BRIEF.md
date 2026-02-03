# Brief: E2E Test Failure Resolution - Ralph Loop

## Introduction

Systematically fix 95 failed E2E tests by iterating through `frontend/test/e2e/FAILED_TESTS.md`. Each Ralph iteration processes ONE test: verify the failure, analyze root cause, implement fix, verify resolution, and track progress with inline comments. The goal is to restore test suite health while identifying whether failures are due to outdated test code or actual application bugs.

## Objectives

- Process failed E2E tests one at a time in sequential order from FAILED_TESTS.md
- Distinguish between test code issues (outdated assertions, wrong selectors) and application bugs
- Fix test code issues by updating the test to match current application behavior
- For application bugs: mark test as `.skip()`, document findings, mark as completed
- Track all findings and attempted fixes as inline comments in FAILED_TESTS.md
- Verify each fix by running the specific test using `scripts/run-playwright-tests.sh`
- Maintain progress tracking with checkbox completion status

## Tasks

### TASK-001: Read FAILED_TESTS.md and identify next test to fix
**Description:** As Ralph, I need to find the first uncompleted test in FAILED_TESTS.md so I know which test to work on in this iteration.

**Acceptance Criteria:**
- [ ] Read `frontend/test/e2e/FAILED_TESTS.md` file
- [ ] Locate first test with `[ ]` checkbox (not completed)
- [ ] Extract test name, file path, and error message
- [ ] Check if test already has comments with notes from previous attempts
- [ ] If no previous notes exist, proceed to TASK-002
- [ ] If previous notes exist, read and consider them, skip to TASK-003
- [ ] Testing passes
- [ ] Linting passes

### TASK-002: Verify test failure behavior
**Description:** As Ralph, I need to run the failing test to confirm it exhibits the documented error so I can validate the failure before attempting a fix.

**Acceptance Criteria:**
- [ ] Run test using `scripts/run-playwright-tests.sh` with module parameter and test name filter
- [ ] Example command: `./scripts/run-playwright-tests.sh catalog "should handle clearing filters"`
- [ ] Confirm test fails with same or similar error as documented
- [ ] If error differs significantly, note the new error behavior
- [ ] Capture relevant error output for analysis
- [ ] Testing passes
- [ ] Linting passes

### TASK-003: Analyze root cause of test failure
**Description:** As Ralph, I need to determine whether the failure is caused by outdated test code or an actual application bug so I can choose the correct fix strategy.

**Acceptance Criteria:**
- [ ] Read the test file to understand what it's testing
- [ ] Analyze error message to identify failure point (timeout, selector not found, assertion mismatch)
- [ ] Check if related application code exists and matches test expectations
- [ ] Review helper functions and imports used by the test
- [ ] Determine if issue is: (1) test code outdated, (2) application bug, (3) missing helper/infrastructure
- [ ] Document analysis in internal notes for TASK-004
- [ ] Testing passes
- [ ] Linting passes

### TASK-004: Implement fix based on root cause
**Description:** As Ralph, I need to fix the identified issue using the appropriate strategy (fix test code, mark as skip for app bug, or fix helper).

**Acceptance Criteria:**
- [ ] If test code is outdated: update test assertions, selectors, or expectations to match current app behavior
- [ ] If test fails due to invalid test data, use playwright mcp server to discover some test data for yourself and use them
- [ ] If application bug is found: add `.skip()` to test with comment explaining the bug, skip to TASK-006
- [ ] If helper is missing/broken: fix the helper function or add missing export
- [ ] Use Edit tool to make precise changes to test or application files
- [ ] Keep changes minimal and focused on the specific failure
- [ ] Do not refactor or improve unrelated code
- [ ] Testing passes
- [ ] Linting passes

### TASK-005: Verify fix by running the test
**Description:** As Ralph, I need to run the fixed test to confirm it now passes and the issue is resolved.

**Acceptance Criteria:**
- [ ] Run test using `scripts/run-playwright-tests.sh` with module and test name filter
- [ ] Confirm test passes without errors
- [ ] If test still fails: document attempted fix and reason for failure in FAILED_TESTS.md comment, mark checkbox as `[ ]` (NOT completed), skip to TASK-007 (end iteration)
- [ ] If test passes: proceed to TASK-006
- [ ] Testing passes
- [ ] Linting passes

### TASK-006: Update FAILED_TESTS.md with completion status
**Description:** As Ralph, I need to mark the test as completed in FAILED_TESTS.md so progress is tracked and the next iteration starts with the next test.

**Acceptance Criteria:**
- [ ] Edit `frontend/test/e2e/FAILED_TESTS.md`
- [ ] Change test checkbox from `[ ]` to `[x]`
- [ ] If test was marked as `.skip()` due to app bug: add inline comment explaining findings and why it's skipped
- [ ] If test was fixed by updating test code: optionally add brief comment about what was changed
- [ ] Ensure markdown formatting remains intact
- [ ] Testing passes
- [ ] Linting passes

### TASK-007: End iteration
**Description:** As Ralph, I need to complete this iteration so the next iteration can start fresh with the next uncompleted test.

**Acceptance Criteria:**
- [ ] Confirm FAILED_TESTS.md has been updated (either marked completed or has notes about failed fix attempt)
- [ ] Summarize what was accomplished in this iteration (test name, outcome: fixed/skipped/needs-more-work)
- [ ] State clearly: "Iteration complete. Ready for next iteration."
- [ ] Do not continue to the next test - stop here
- [ ] Testing passes
- [ ] Linting passes

## Out of Scope

- Fixing multiple tests in one iteration (strictly one test per iteration)
- Running entire test suites or modules for verification (only run the specific test being fixed)
- Refactoring or improving test code beyond what's needed to fix the failure
- Fixing application bugs beyond simple/obvious issues (mark as `.skip()` and document instead)
- Investigating staging environment issues or infrastructure problems
- Modifying test data fixtures or shared helper functions without clear need

## Implementation Notes

### Test Execution Command Pattern
```bash
# Run specific test by module and name filter
./scripts/run-playwright-tests.sh <module> "<test-name-substring>"

# Examples:
./scripts/run-playwright-tests.sh catalog "should handle clearing filters"
./scripts/run-playwright-tests.sh issued-invoices "Invoice ID filter with Enter"
./scripts/run-playwright-tests.sh core "should show Classify Invoice button"
```

### FAILED_TESTS.md Comment Format
When adding notes about failed fix attempts, use inline comments:
```markdown
### [ ] should filter products by name using Filter button
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded`
- **Notes**: Attempted fix by increasing timeout to 10000ms - test still fails. Root cause appears to be backend performance issue on staging. API `/api/catalog` responding slowly (>8s). Recommended: investigate backend query performance or mark as skip.
```

### Marking Tests as Skipped for App Bugs
```typescript
test.skip('should filter products by name using Filter button', async ({ page }) => {
  // SKIPPED: Backend performance issue on staging
  // API /api/catalog responds in >8s causing timeout
  // See FAILED_TESTS.md for details
  // TODO: Fix backend query performance before re-enabling
});
```

### Key Files
- **Test tracking**: `frontend/test/e2e/FAILED_TESTS.md`
- **Test execution**: `scripts/run-playwright-tests.sh`
- **Test files**: `frontend/test/e2e/<module>/*.spec.ts`
- **Test helpers**: `frontend/test/e2e/helpers/*.ts`
- **Test fixtures**: `frontend/test/e2e/fixtures/test-data.ts`

### Common Failure Patterns (from report)
1. **TimeoutError: page.waitForResponse** - Backend performance or API availability
2. **Main element not visible** (issued-invoices) - Navigation/routing issue
3. **navigateToClassificationHistory is not a function** - Missing helper export
4. **expect(received).toBeGreaterThan(0)** - Test data missing or filters broken

### Decision Logic for Fix Strategy
```
If error = "is not a function" → Fix missing export/import
If error = "element not visible" + systematic across module → Fix navigation helper
If error = "timeout waiting for response" + staging only → Skip with comment about backend
If error = "assertion failed" + app behavior changed → Update test assertions
If error = "test data missing" → Update test fixtures or skip with comment
```

### Constraints
- Each iteration must complete within one context window
- Only ONE test processed per iteration
- Always end iteration after completing/documenting a test
- Never continue to next test in same iteration
- Use `Edit` tool for surgical changes, not `Write`
- Preserve existing test structure and patterns
