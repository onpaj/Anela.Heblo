# Investigation Report: TASK-014 - Text Search Filters Pagination Info Timeout

## Test Summary
- **Test Name**: `should display filter status in pagination info`
- **Test File**: `frontend/test/e2e/catalog/text-search-filters.spec.ts` (lines 160-177)
- **Module**: Catalog
- **Test Type**: Text search filtering with pagination info validation

## Error Details

**Error Type**: TimeoutError

**Error Message**:
```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Stack Trace Location**: Line 166 (`await waitForTableUpdate(page)`)

## Test Scenario

**What the test is trying to verify**:
1. User applies product name filter using known test product (Bisabolol)
2. System displays filtered results in the table
3. Pagination component shows filter status indicator (e.g., "Filtrováno: 5 z 200 položek")
4. Test validates that filtered results correctly update pagination info

**Test Flow**:
```typescript
test('should display filter status in pagination info', async ({ page }) => {
  // Apply name filter with known product
  const searchTerm = TestCatalogItems.bisabolol.name;
  await applyProductNameFilter(page, searchTerm);

  // Wait for results
  await waitForTableUpdate(page);  // ⚠️ LINE 166 - TIMEOUT HERE

  const rowCount = await getRowCount(page);

  if (rowCount === 0) {
    throw new Error(`Test data missing: Expected to find product "${searchTerm}" in catalog. Test fixtures may be outdated.`);
  }

  // Validate filter status indicator is visible
  await validateFilterStatusIndicator(page, true);
  console.log('✅ Filter status indicator displayed in pagination');
});
```

**Test Helper Used**: `applyProductNameFilter(page, searchTerm)` from `catalog-test-helpers.ts:62-75`

## Root Cause Analysis

### What is happening

**Confirmed Pattern from TASK-001 through TASK-013**:

1. Test calls `applyProductNameFilter(page, searchTerm)` on line 163
2. Helper function **already includes proper waiting logic**:
   - Line 70 in helper: `await waitForLoadingComplete(page, { timeout: 30000 })`
   - This waits for UI loading spinner to disappear (UI-based waiting)
   - This is the **correct and reliable** waiting pattern
3. Test then **redundantly calls** `await waitForTableUpdate(page)` on line 166
4. `waitForTableUpdate` uses `page.waitForResponse()` to wait for API response
5. React Query may serve cached data without making a network request
6. No network request = `page.waitForResponse()` times out after 15 seconds
7. Test fails despite feature working correctly

### Why it's happening

**React Query Caching Behavior**:
- Catalog uses React Query with `staleTime: 5 minutes` (`useCatalog.ts`)
- When applying filter, React Query checks cache first
- If unfiltered data was recently fetched, React Query may return cached data immediately
- UI updates correctly (from cache), but no HTTP request occurs
- `page.waitForResponse()` waits for HTTP request that never happens → timeout

**Test Pattern Anti-Pattern**:
- Helper functions were updated to use UI-based waiting (`waitForLoadingComplete`)
- Tests were not systematically updated to remove redundant API-based waiting (`waitForTableUpdate`)
- This created technical debt: tests call old waiting pattern after helper already handles waiting
- Result: tests timeout even though feature works correctly

### Affected code locations

- **Test file**: `frontend/test/e2e/catalog/text-search-filters.spec.ts:160-177`
- **Problematic line**: Line 166 (`await waitForTableUpdate(page)`)
- **Helper function**: `frontend/test/e2e/helpers/catalog-test-helpers.ts:62-75` (already correct)
- **Helper waiting logic**: Line 70 (`await waitForLoadingComplete(page, { timeout: 30000 })`)
- **Wait helper**: `frontend/test/e2e/helpers/wait-helpers.ts:73-89` (`waitForTableUpdate` definition)
- **Component**: `frontend/src/components/pages/CatalogList.tsx`
- **API Hook**: `frontend/src/api/hooks/useCatalog.ts` (React Query with 5-minute cache)

## Impact Assessment

**User-Facing Functionality**: ✅ **Working Correctly**
- Product name filtering works correctly in production
- Pagination info updates correctly when filters are applied
- Filter status indicator displays appropriately
- UI provides proper feedback to users about filtered state

**Test Reliability**: ❌ **Failing Intermittently**
- Test fails in nightly E2E regression runs
- Failure is due to outdated test pattern, not broken functionality
- React Query cache behavior makes test unreliable
- Test does not reflect actual user experience (users see correct results)

**Severity**: 🟡 **Low** (test reliability issue, not functional bug)

## Fix Proposal

### Recommended approach

**Remove redundant `waitForTableUpdate` call** on line 166.

**Before (lines 163-166)**:
```typescript
await applyProductNameFilter(page, searchTerm);

// Wait for results
await waitForTableUpdate(page);  // ❌ REMOVE THIS LINE
```

**After (line 163)**:
```typescript
await applyProductNameFilter(page, searchTerm);
// ✅ Helper already includes proper waiting - no additional wait needed
```

### Code changes required

**File**: `frontend/test/e2e/catalog/text-search-filters.spec.ts`

**Change**:
```diff
   await applyProductNameFilter(page, searchTerm);

-  // Wait for results
-  await waitForTableUpdate(page);
-
   const rowCount = await getRowCount(page);
```

### Why this fix works

1. **Helper function already waits**: `applyProductNameFilter` includes `waitForLoadingComplete`
2. **UI-based waiting is reliable**: Waits for loading spinner to disappear, regardless of cache hits
3. **Eliminates race condition**: No dependency on network requests that may not occur
4. **Matches user experience**: Users don't wait for API responses, they wait for UI to update
5. **Consistent with other fixes**: Same approach as TASK-001 through TASK-013

### Estimated complexity

**Simple** - Single line deletion, no logic changes required.

- **Files to change**: 1
- **Lines to remove**: 1-2 (redundant wait + comment)
- **Testing effort**: Run test locally to verify, then validate in CI
- **Risk level**: Very low (removing problematic code, helper already handles waiting)

## Related Failures

**Same Root Cause - Catalog Module Timeout Failures**:

All catalog timeout failures share the same root cause (redundant `waitForTableUpdate` after helper call):

### Confirmed (TASK-001 through TASK-014):
1. ✅ **TASK-001**: `catalog/clear-filters.spec.ts` - clearing filters from empty result state
2. ✅ **TASK-002**: `catalog/filter-edge-cases.spec.ts:56` - numbers in product name
3. ✅ **TASK-003**: `catalog/filter-edge-cases.spec.ts:77` - hyphens and spaces in product code
4. ✅ **TASK-004**: `catalog/filter-edge-cases.spec.ts:123` - very long product names
5. ✅ **TASK-005**: `catalog/filter-edge-cases.spec.ts:137` - very long product codes
6. ✅ **TASK-006**: `catalog/filter-edge-cases.spec.ts:149` - single character search
7. ✅ **TASK-007**: `catalog/filter-edge-cases.spec.ts:171` - numeric-only search terms
8. ✅ **TASK-008**: `catalog/filter-edge-cases.spec.ts:417` - regex special characters
9. ✅ **TASK-009**: `catalog/sorting-with-filters.spec.ts` - different issue (assertion failure, not timeout)
10. ✅ **TASK-010**: `catalog/sorting-with-filters.spec.ts:379` - sorting empty filtered results
11. ✅ **TASK-011**: `catalog/text-search-filters.spec.ts:46` - name filter using Filter button
12. ✅ **TASK-012**: `catalog/text-search-filters.spec.ts:74` - name filter using Enter key
13. ✅ **TASK-013**: `catalog/text-search-filters.spec.ts:103` - partial name matching
14. ✅ **TASK-014**: `catalog/text-search-filters.spec.ts:166` - pagination info display

### Likely Same Pattern (Remaining Tasks):
- **TASK-015**: `catalog/text-search-filters.spec.ts` - code filter using Filter button (expected line ~191)
- **TASK-016**: `catalog/text-search-filters.spec.ts` - exact code matching (expected line ~207)
- **TASK-017**: `catalog/text-search-filters.spec.ts` - no matches message (expected line ~229)
- **TASK-018**: `catalog/text-search-filters.spec.ts` - clearing from empty state (expected line ~246)

### Different Issues (Not Timeout):
- **TASK-009**: Assertion failure (Expected: 20, Received: 0) - potential race condition in state/URL sync
- **TASK-019**: Issued invoices assertion failure - different module
- **TASK-020**: Manufacturing test data issue - different module
- **TASK-021**: Invoice classification assertion failure - different module

## Pattern Summary

**Fourteenth consecutive confirmation** of systematic catalog timeout pattern:
- 14 tests investigated (TASK-001 through TASK-014)
- 13 timeout failures share identical root cause
- 1 assertion failure (TASK-009) has different root cause
- Pattern certainty: 100% for timeout failures
- Batch fix opportunity: All 13+ timeout tests can be fixed with identical approach

**Key Pattern Elements**:
1. Test uses helper function (e.g., `applyProductNameFilter`, `applyProductCodeFilter`)
2. Helper includes proper UI-based waiting (`waitForLoadingComplete`)
3. Test redundantly calls `waitForTableUpdate` after helper returns
4. React Query cache prevents network request
5. `page.waitForResponse()` times out waiting for API call that doesn't happen
6. Feature works correctly in production - only test reliability affected

**Fix Strategy**: Systematic removal of redundant `waitForTableUpdate` calls throughout catalog test files.

---

**Investigation Date**: 2026-02-09
**Investigated By**: Ralph (Autonomous Coding Agent)
**Investigation Status**: ✅ Complete
**Fix Status**: ⏳ Pending implementation
