# TASK-009: Investigation Report - Sorting with Filters Maintain Filter Assertion Failure

## Test Summary

**Test Name:** `should maintain filter when changing sort direction`
**File Location:** `frontend/test/e2e/catalog/sorting-with-filters.spec.ts:188-217`
**Module:** Catalog
**Type:** Assertion Failure (not timeout)

## Error Details

```
Error: expect(received).toBe(expected) // Object.is equality

Expected: 20
Received: 0

  200 |
  201 |       currentRowCount = await getRowCount(page);
> 202 |       expect(currentRowCount).toBe(initialRowCount);
      |                               ^
```

**Error Location:** `sorting-with-filters.spec.ts:201`

## Test Scenario

The test verifies that product type filters are maintained when changing sort direction:

1. Apply product type filter: "Materiál" (line 190)
2. Capture initial row count (line 192) - Expected to be > 0
3. Click "Název produktu" header to sort ascending (line 197)
4. Wait for table update (line 198)
5. **FAILS HERE:** Assert current row count equals initial count (line 201)
   - Expected: 20 (or whatever `initialRowCount` was)
   - Received: 0 (no results after sorting)
6. Click header again to sort descending (line 204)
7. Assert row count still matches initial (line 208)

## Root Cause Analysis

### What is happening

After applying a product type filter ("Materiál"), the test successfully retrieves results (`initialRowCount > 0`). However, when clicking the "Název produktu" column header to sort, the table becomes empty (0 rows), causing the assertion to fail.

### Why it's happening

**Different from TASK-001 through TASK-008**: This is NOT a React Query caching issue or redundant `waitForTableUpdate` call. This is an assertion failure indicating the actual filtering is being lost.

**Analysis of code flow:**

1. **Test applies filter correctly** (line 190):
   ```typescript
   await selectProductType(page, 'Materiál');
   ```
   - This calls the helper which selects the product type and waits for loading to complete
   - Initial row count is captured successfully (confirming filter worked)

2. **Test waits for sort properly** (lines 197-198):
   ```typescript
   const nameHeader = page.locator('th').filter({ hasText: 'Název produktu' }).first();
   await nameHeader.click();
   await waitForTableUpdate(page);
   ```
   - Helper uses `waitForSearchResults(page, { endpoint: '/api/catalog' })` which waits for API response
   - This is the CORRECT pattern (waiting for API response after manual header click)

3. **Component behavior** (`CatalogList.tsx:160-169`):
   ```typescript
   const handleSort = (column: string) => {
     if (sortBy === column) {
       setSortDescending(!sortDescending);
     } else {
       setSortBy(column);
       setSortDescending(false);
     }
     setPageNumber(1); // Reset to page 1 when sort changes
     // React Query will automatically refetch when sortBy or sortDescending changes
   };
   ```
   - Sort handler correctly resets to page 1
   - React Query refetches automatically when `sortBy` or `sortDescending` changes
   - **Filter state is NOT cleared** - `productTypeFilter` remains set

4. **API call** (`useCatalogQuery` hook):
   ```typescript
   const { data, isLoading, error, refetch } = useCatalogQuery(
     productNameFilter,
     productCodeFilter,
     productTypeFilter,
     pageNumber,
     pageSize,
     sortBy,
     sortDescending,
   );
   ```
   - All parameters including `productTypeFilter` are passed to API
   - When sort changes, query should include all filter parameters

### Possible root causes

**Option 1: React Query Cache Issue (Different from TASK-001-008)**
- Unlike previous tasks, this appears to be a **state desynchronization** issue
- When sort state changes, React Query may serve stale cached data
- The cache key might not properly include all filter parameters
- Sort change might trigger cache invalidation that drops filter parameters

**Option 2: Test Data Availability Issue**
- Staging environment may have very few or zero "Materiál" items
- If `initialRowCount` is low (e.g., 1-5 items), sorting might expose pagination edge case
- Initial filter might return results on page 1, but sorting changes results distribution

**Option 3: Race Condition in State Updates**
- Component has complex state synchronization with URL params (lines 186-241)
- Multiple `useEffect` hooks manage state and URL params
- Sorting triggers multiple state updates that might race with filter state
- URL params might be getting cleared during sort state update

**Option 4: Backend Query Construction Issue**
- When both `productType` filter and `sortBy` are present, backend might not construct query correctly
- SQL query might prioritize sorting over filtering (unlikely but possible)
- API might not be receiving `productType` parameter during sort request

### Most likely cause

Based on the code analysis, **Option 3 (Race Condition)** is most likely:

1. Test applies `productType` filter → state updates → URL updates → API called with filter
2. Test clicks sort header → `handleSort` called → `sortBy` state changes → `pageNumber` reset to 1
3. Two `useEffect` hooks fire (lines 186-204 and 208-241)
4. Effect at 186-204 updates URL params with new sort/page, but might not preserve filter params
5. Effect at 208-241 syncs state from URL params, potentially clearing filter if URL was incomplete
6. React Query refetches with incomplete parameters → no filters applied → empty results

**Evidence for race condition:**
- Line 203: `setSearchParams(params, { replace: true })` uses `replace: true`
- Line 240: Comment warns about "infinite loops" in useEffect dependency management
- Complex bidirectional sync between state and URL params creates multiple update paths
- Sort state change triggers both effects, potentially in unpredictable order

### Affected code locations

1. **Test file:** `frontend/test/e2e/catalog/sorting-with-filters.spec.ts:188-217`
2. **Component:** `frontend/src/components/pages/CatalogList.tsx:160-169` (handleSort)
3. **State sync effect:** `frontend/src/components/pages/CatalogList.tsx:186-204`
4. **URL sync effect:** `frontend/src/components/pages/CatalogList.tsx:208-241`
5. **Test helper:** `frontend/test/e2e/helpers/catalog-test-helpers.ts:327-347` (waitForTableUpdate)

## Impact Assessment

**User-facing functionality affected:**
- Users applying product type filter and then sorting may lose their filter
- This is a CRITICAL bug if it occurs in production
- Users would need to re-apply filters after every sort operation
- Significantly degrades user experience for catalog filtering

**Severity:** HIGH - This is a real functional bug, not just a test pattern issue

## Fix Proposal

### Recommended approach: Investigate actual behavior in staging

**Before proposing code fixes, need to determine if this is:**
1. A real production bug (filters lost when sorting)
2. A test data issue (no "Materiál" items in staging)
3. A test timing issue (race condition in test, not in app)

**Investigation steps:**
1. Run test with `--headed --debug` to observe actual behavior
2. Check network requests to see if `productType` parameter is included in sort API call
3. Verify staging has sufficient "Materiál" items for testing
4. Check if `initialRowCount` is actually > 0 when test runs

### Proposed fix (if confirmed as race condition bug)

**Option A: Fix URL parameter preservation in handleSort**

```typescript
// In CatalogList.tsx:160-169
const handleSort = (column: string) => {
  if (sortBy === column) {
    setSortDescending(!sortDescending);
  } else {
    setSortBy(column);
    setSortDescending(false);
  }
  setPageNumber(1);

  // Immediately update URL with ALL current filter state to prevent race condition
  const params = new URLSearchParams(searchParams);
  if (productNameFilter) params.set("productName", productNameFilter);
  if (productCodeFilter) params.set("productCode", productCodeFilter);
  if (productTypeFilter) params.set("productType", productTypeFilter);
  if (column) params.set("sortBy", column);
  params.set("sortDesc", (sortBy === column ? !sortDescending : false).toString());
  params.delete("page"); // Reset to page 1
  setSearchParams(params, { replace: true });
};
```

**Option B: Simplify state/URL synchronization**

Refactor to reduce number of `useEffect` hooks and potential race conditions:
- Single source of truth (either state or URL, not both)
- Consolidate URL update logic into single effect
- Ensure all state updates happen atomically

**Option C: Add test data validation**

If issue is test data availability:
```typescript
const initialRowCount = await getRowCount(page);

if (initialRowCount === 0) {
  throw new Error('Test data missing: Expected to find products with type "Materiál"');
}

// Continue with test...
```

### Estimated complexity

**Investigation:** Simple (run test in debug mode, check network, verify data)
**Fix (if race condition):** Medium (requires careful state management refactoring)
**Fix (if test data):** Simple (add test data validation or use different filter value)

## Related Failures

**TASK-010:** "should handle sorting empty filtered results" (timeout)
- Different error (timeout vs assertion)
- But same test file, related to sorting + filtering interaction
- May share some root cause elements

**Pattern detection:**
- TASK-009 is the FIRST assertion failure (vs timeout)
- TASK-009 is the FIRST test where functional correctness is questioned
- Previous 8 tests had test pattern issues, not feature bugs
- This test reveals potential state management issue in component

## Test Data Considerations

**Required test data:**
- Products with `productType = "Materiál"` must exist in staging database
- Minimum 20+ items to properly test pagination and sorting
- Items should have diverse names to test sorting order

**Validation needed:**
- Check `docs/testing/test-data-fixtures.md` for Materiál items
- Verify staging database has sufficient Materiál products
- Consider using more common product type (e.g., "Produkt") if Materiál data is sparse

## Recommendations

1. **Immediate:** Run test in debug mode to observe actual network requests and state changes
2. **Verify:** Check staging database for "Materiál" product availability
3. **Validate:** Confirm `initialRowCount` value when test runs
4. **Consider:** Using "Produkt" filter instead of "Materiál" if data is insufficient
5. **If bug confirmed:** Prioritize fix as this is user-facing functionality issue
6. **If test data issue:** Add explicit test data validation with clear error messages

## Next Steps

1. Execute test with `--headed --debug` flags
2. Monitor network tab for `/api/catalog` requests during sort operation
3. Verify `productType` parameter is included in API request
4. Check response to see if results are actually empty or test timing is wrong
5. Based on findings, proceed with appropriate fix (code or test data)

## Notes

- This is qualitatively different from TASK-001 through TASK-008
- Not a test pattern issue - this is investigating potential functional bug
- Test is using correct waiting patterns (`waitForTableUpdate` after manual click)
- Assertion failure indicates actual result mismatch, not test timing
- High priority for investigation and fix if confirmed as production bug
