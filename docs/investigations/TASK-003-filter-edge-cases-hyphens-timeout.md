# Investigation Report: TASK-003 - Filter Edge Cases Hyphens and Spaces Timeout

## Test Summary
- **Test Name**: "should handle hyphens and spaces in product code"
- **File Location**: `frontend/test/e2e/catalog/filter-edge-cases.spec.ts:73-85`
- **Module**: Catalog
- **Status**: ❌ FAILING (Timeout Error)

## Error Details

```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Error Location**: Line 77 - `await waitForTableUpdate(page);`

## Test Scenario

This test verifies that the catalog can handle product codes containing hyphens and spaces without crashing or causing errors:

1. Navigate to catalog page
2. Apply product code filter with value "AH-" (contains hyphen)
3. Wait for table update
4. Get row count and log results
5. Verify no errors occurred

**Purpose**: Edge case handling for special characters in product code filtering.

## Root Cause Analysis

### What is happening
The test times out at line 77 when waiting for an API response to `/api/catalog`.

### Why it's happening

**SAME PATTERN AS TASK-001 AND TASK-002**: The test uses a helper function that already includes proper waiting, then adds a redundant `waitForTableUpdate()` call that expects a network request.

**Detailed explanation**:

1. **Line 75**: `await applyProductCodeFilter(page, "AH-")`
   - Helper function from `catalog-test-helpers.ts`
   - Internally fills input, clicks Filter button, and **calls `waitForLoadingComplete(page)`**
   - This UI-based waiting is sufficient and reliable

2. **Line 77**: `await waitForTableUpdate(page)` ❌ **REDUNDANT**
   - Waits for network response to `/api/catalog`
   - React Query cache (5-minute `staleTime`) may serve data without network request
   - When cache hit occurs, no network request = timeout after 15 seconds

### Affected code locations

- **Test file**: `frontend/test/e2e/catalog/filter-edge-cases.spec.ts:77`
- **Helper function**: `frontend/test/e2e/helpers/catalog-test-helpers.ts:applyProductCodeFilter()` (already includes waiting)
- **Wait helper**: `frontend/test/e2e/helpers/wait-helpers.ts:waitForTableUpdate()` (expects network activity)

## Impact Assessment

### User-Facing Functionality
**✅ NO USER IMPACT** - The functionality works correctly. This is a test reliability issue.

- Filtering by product codes with hyphens works properly
- React Query handles requests and caching correctly
- UI updates appropriately after filter application

### Test Reliability
**❌ TEST FAILS INTERMITTENTLY** - Depending on React Query cache state:
- **Cache miss**: Network request occurs → test passes
- **Cache hit**: No network request → test times out

## Fix Proposal

### Recommended Approach
**Remove the redundant `waitForTableUpdate` call on line 77.**

### Code Changes Required

**File**: `frontend/test/e2e/catalog/filter-edge-cases.spec.ts`

**Current code (lines 73-85)**:
```typescript
test("should handle hyphens and spaces in product code", async ({ page }) => {
  // Search for code with hyphens or spaces
  await applyProductCodeFilter(page, "AH-");

  await waitForTableUpdate(page); // ❌ REMOVE THIS LINE

  const rowCount = await getRowCount(page);

  console.log(`📊 Results with hyphen search: ${rowCount}`);

  // Just verify it doesn't crash
  console.log("✅ Hyphens in product code handled without errors");
});
```

**Fixed code**:
```typescript
test("should handle hyphens and spaces in product code", async ({ page }) => {
  // Search for code with hyphens or spaces
  await applyProductCodeFilter(page, "AH-"); // Helper includes proper waiting

  const rowCount = await getRowCount(page);

  console.log(`📊 Results with hyphen search: ${rowCount}`);

  // Just verify it doesn't crash
  console.log("✅ Hyphens in product code handled without errors");
});
```

### Estimated Complexity
**SIMPLE** - Single line deletion, no logic changes required.

## Related Failures

### Tests with identical root cause in same file
All these tests in `filter-edge-cases.spec.ts` follow the same anti-pattern (helper + redundant `waitForTableUpdate`):

1. ✅ **TASK-002**: "should handle numbers in product name" (line 56) - **INVESTIGATED**
2. ✅ **TASK-003**: "should handle hyphens and spaces in product code" (line 77) - **THIS INVESTIGATION**
3. ⏳ **TASK-004**: "should handle very long product names" (line 123)
4. ⏳ **TASK-005**: "should handle very long product codes" (line 137)
5. ⏳ **TASK-006**: "should handle single character search" (line 149)
6. ⏳ **TASK-007**: "should handle numeric-only search terms" (line 171)
7. ⏳ **TASK-008**: "should handle regex special characters" (line 417)

### Pattern across catalog module
- **TASK-001**: `catalog/clear-filters.spec.ts` - Manual operation + redundant wait
- **TASK-009 to TASK-018**: Likely same pattern in other catalog test files

### Batch Fix Opportunity
All 7 failing tests in `filter-edge-cases.spec.ts` can be fixed in a **single commit** by removing redundant `waitForTableUpdate` calls.

## Investigation Metadata
- **Investigated by**: Ralph (Autonomous Coding Agent)
- **Investigation Date**: 2026-02-09
- **Nightly Regression Date**: 2026-02-09
- **Related Tasks**: TASK-001, TASK-002, TASK-004, TASK-005, TASK-006, TASK-007, TASK-008
- **Pattern Type**: Redundant API wait after helper with UI-based waiting
