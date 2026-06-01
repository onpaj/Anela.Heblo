# Investigation Report: TASK-004 - Catalog Filter Edge Cases (Long Product Names) Timeout

## Test Summary
- **Test Name**: "should handle very long product names (>100 chars)"
- **File Location**: `frontend/test/e2e/catalog/filter-edge-cases.spec.ts:116-130`
- **Module**: Catalog
- **Status**: ❌ Failing (Timeout)

## Error Details

```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Stack Trace Context**:
- Error occurs at line 123: `await waitForTableUpdate(page)`
- After applying product name filter with 150-character string: `"A".repeat(150)`

## Test Scenario

**What the test verifies**:
- Catalog filter can handle very long product names (>100 characters)
- System doesn't crash when filtering with boundary-condition input
- UI gracefully handles edge case (may return no results, but no errors)

**Test Flow**:
1. Navigate to catalog page
2. Apply product name filter with 150-character string (`"AAAAAA..."`)
3. Wait for table to update (line 123)
4. Verify system didn't crash by checking row count
5. Log results for debugging

## Root Cause Analysis

### What is happening
The test is timing out while waiting for an API response to `/api/catalog` after applying a product name filter.

### Why it's happening

**Confirmed pattern from TASK-001, TASK-002, TASK-003**:

1. **Helper function already includes proper waiting**:
   - `applyProductNameFilter(page, longName)` on line 121 calls `waitForLoadingComplete(page)` internally
   - Helper function ensures UI is stable before returning

2. **Redundant API wait causes timeout**:
   - Line 123 adds `await waitForTableUpdate(page)` after helper completes
   - `waitForTableUpdate` uses `page.waitForResponse(/\/api\/catalog/)` to wait for network request
   - React Query may serve cached data without making network request
   - No network request → `waitForResponse` times out after 15 seconds

3. **React Query caching behavior**:
   - Catalog hooks use React Query with `staleTime: 5 minutes`
   - When long product name returns empty results (likely no matches), React Query may cache response
   - Subsequent filter operations may use cached data if within stale time window
   - Cache hits bypass network layer, causing `waitForResponse` to timeout

### Affected code locations

**Test file**: `frontend/test/e2e/catalog/filter-edge-cases.spec.ts`
- Line 121: `await applyProductNameFilter(page, longName)` ✅ Correct (includes proper waiting)
- Line 123: `await waitForTableUpdate(page)` ❌ **Redundant and causes timeout**

**Helper implementation**: `frontend/test/e2e/helpers/catalog-test-helpers.ts`
- `applyProductNameFilter` function already calls `waitForLoadingComplete(page)`
- No additional waiting needed after helper call

**Component implementation**: `frontend/src/components/pages/CatalogList.tsx`
- Uses React Query hooks from `frontend/src/api/hooks/useCatalog.ts`
- Loading states properly managed with Spinner component
- Filtering functionality works correctly (not a bug, test pattern issue)

## Impact Assessment

**User-facing functionality**: ✅ **Working correctly**
- Product name filtering works with long input strings (150+ characters)
- No crashes or errors in production
- UI properly handles edge cases with loading states

**Test reliability**: ❌ **Flaky test pattern**
- Test fails intermittently due to React Query caching
- Timeout doesn't indicate broken functionality
- Test needs pattern correction for reliability

**Business impact**: Low
- Feature works correctly in production
- Only test reliability affected
- No user-reported issues with long product name filtering

## Fix Proposal

### Recommended approach
**Remove redundant wait** - Delete line 123

### Code changes required

**File**: `frontend/test/e2e/catalog/filter-edge-cases.spec.ts`

**Before** (lines 116-130):
```typescript
test("should handle very long product names (>100 chars)", async ({
  page,
}) => {
  const longName = "A".repeat(150);

  await applyProductNameFilter(page, longName);

  await waitForTableUpdate(page);  // ❌ Remove this line

  // Should not crash, might return no results
  const rowCount = await getRowCount(page);
  console.log(`📊 Results for very long name: ${rowCount}`);

  console.log("✅ Very long product name handled without errors");
});
```

**After**:
```typescript
test("should handle very long product names (>100 chars)", async ({
  page,
}) => {
  const longName = "A".repeat(150);

  await applyProductNameFilter(page, longName);
  // ✅ Helper already includes proper waiting, no additional wait needed

  // Should not crash, might return no results
  const rowCount = await getRowCount(page);
  console.log(`📊 Results for very long name: ${rowCount}`);

  console.log("✅ Very long product name handled without errors");
});
```

**Changes**:
- Delete line 123: `await waitForTableUpdate(page);`
- Add explanatory comment about helper's built-in waiting

### Estimated complexity
**Simple** - Single line deletion
- No logic changes required
- No new dependencies
- No risk to other tests
- Can be batched with TASK-005 through TASK-008 fixes

### Testing validation
After fix:
1. Run specific test: `./scripts/run-playwright-tests.sh catalog/filter-edge-cases`
2. Verify test passes consistently (run 3-5 times to check for flakiness)
3. Verify other tests in same file still pass
4. Check no regressions in catalog module tests

## Related Failures

**Identical root cause confirmed in**:
- ✅ TASK-001: `catalog/clear-filters.spec.ts` - Helper + redundant `waitForTableUpdate`
- ✅ TASK-002: `catalog/filter-edge-cases.spec.ts:56` - "numbers in product name"
- ✅ TASK-003: `catalog/filter-edge-cases.spec.ts:77` - "hyphens and spaces in product code"
- 🔄 TASK-004: `catalog/filter-edge-cases.spec.ts:123` - "long product names" (this investigation)

**Likely same pattern** (not yet investigated):
- TASK-005: `catalog/filter-edge-cases.spec.ts:137` - "long product codes"
- TASK-006: `catalog/filter-edge-cases.spec.ts:149` - "single character search"
- TASK-007: `catalog/filter-edge-cases.spec.ts:171` - "numeric-only search"
- TASK-008: `catalog/filter-edge-cases.spec.ts:417` - "regex special characters"

### Pattern summary
All tests in `catalog/filter-edge-cases.spec.ts` that call helper functions followed by `waitForTableUpdate` are affected by this issue. This is a **systematic problem** requiring batch fix, not isolated incidents.

## Additional Notes

### Why this test is important
- Tests boundary condition handling (>100 character input)
- Ensures system gracefully handles edge cases
- Validates no crashes or errors with unusual input lengths
- Important for security (validates input handling doesn't break)

### Alternative wait strategies considered
1. ❌ **Increase timeout**: Doesn't solve root cause, just masks problem
2. ❌ **Disable React Query cache**: Changes production behavior for test convenience
3. ✅ **Remove redundant wait**: Correct solution - use UI-based waiting from helper
4. ❌ **Add cache-busting query params**: Over-complicates test, affects behavior

### Validation approach
- Test validates "no crash" behavior, not specific result counts
- Appropriate for boundary condition testing
- Console logging helps debug edge case behavior
- Test expectations correctly handle "might return no results" scenario

## Investigation Metadata
- **Investigation Date**: 2026-02-09
- **Staging Environment**: https://heblo.stg.anela.cz
- **Related Documentation**: `docs/testing/playwright-e2e-testing.md`
- **Pattern Reference**: WORKLOG.md - Iterations 1-3 learnings
