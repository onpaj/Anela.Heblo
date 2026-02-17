# Investigation Report: TASK-001 - Catalog Clear Filters Timeout

## Test Summary
- **Test Name**: should handle clearing filters from empty result state
- **File**: `frontend/test/e2e/catalog/clear-filters.spec.ts:233-257`
- **Module**: Catalog

## Error Details
```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Stack Trace Context:**
- The timeout occurs at the second `waitForTableUpdate(page)` call (line 247)
- After clicking the clear button (line 245)
- The test is waiting for an API response from `/api/catalog` endpoint

## Test Scenario

### What the test is trying to verify:
1. Apply a filter that produces no results (`'NONEXISTENTPRODUCT12345'`)
2. Verify the empty state message is displayed
3. Click the "Vymazat" (Clear) button to remove filters
4. Verify that products are shown again (full dataset restored)
5. Verify filter inputs are cleared

### Test Flow:
```typescript
// Line 235: Apply filter that results in empty state
await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');

// Line 237: Wait for table update (first wait - SUCCEEDS)
await waitForTableUpdate(page);

// Line 240: Verify empty state
await validateEmptyState(page);

// Line 243-245: Clear filters
const clearButton = getClearButton(page);
await expect(clearButton).toBeVisible();
await clearButton.click();

// Line 247: Wait for table update (second wait - TIMES OUT)
await waitForTableUpdate(page);
```

## Root Cause Analysis

### What is happening:
The test is timing out while waiting for an API response to `/api/catalog` after clicking the clear button. The `waitForTableUpdate()` helper function uses `page.waitForResponse()` to wait for a successful (status 200) API call to `/api/catalog`.

### Investigation findings:

#### 1. Helper Function Chain:
- `waitForTableUpdate(page)` → `frontend/test/e2e/helpers/catalog-test-helpers.ts:327`
- Calls `waitForSearchResults(page, { endpoint: '/api/catalog' })` → `frontend/test/e2e/helpers/wait-helpers.ts:98`
- Uses `page.waitForResponse(resp => resp.url().includes('/api/catalog') && resp.status() === 200, { timeout: 15000 })`

#### 2. Clear Button Implementation:
- Component: `frontend/src/components/pages/CatalogList.tsx:139-157`
- The `handleClearFilters` function:
  - Clears all filter state variables (lines 140-145)
  - Updates URL parameters (lines 147-153)
  - Calls `await refetch()` to reload data (line 156)

#### 3. API Hook Implementation:
- Hook: `useCatalogQuery` in `frontend/src/api/hooks/useCatalog.ts:123-163`
- Uses React Query's `useQuery` with query keys based on filter parameters
- The `refetch()` function triggers a new API call to `/api/catalog`

### Why it's happening:

**Hypothesis 1: Race Condition with React Query Cache**
When clearing filters from an empty result state, React Query may be:
- Returning cached data immediately instead of making a new network request
- The cache key changes from filtered (empty) to unfiltered (full dataset)
- React Query might serve data from cache without triggering a network request that Playwright can intercept

**Hypothesis 2: Request Already Completed Before waitForResponse**
The `handleClearFilters` is async and calls `await refetch()`, but the test clicks the button and immediately starts waiting. The API request might complete before `page.waitForResponse()` starts listening, especially if:
- React Query has the unfiltered data cached
- The network request is very fast
- There's no explicit loading state between states

**Hypothesis 3: Different Endpoint or Parameters**
The API request might be made to a slightly different URL that doesn't match the pattern `/api/catalog` in the wait condition, such as:
- `/api/catalog?pageNumber=1&pageSize=20` (with query params)
- The wait helper uses `.includes('/api/catalog')` which should match, so this is less likely

**Hypothesis 4: API Request Failure**
The clear operation might be triggering an API request that:
- Returns a non-200 status code
- The test is waiting for status 200, but getting 400, 500, etc.
- Less likely given the first `waitForTableUpdate` succeeds

### Most Likely Root Cause:
**React Query Cache Hit - No Network Request Made**

The `waitForTableUpdate` helper expects a network request to `/api/catalog`, but React Query may be serving the unfiltered catalog data from its cache without making a new network request. This is because:

1. The catalog was already loaded when the page first loaded (full dataset)
2. React Query caches data for 5 minutes (staleTime: 5 * 60 * 1000)
3. When clearing filters, React Query may recognize it has fresh data for the unfiltered query and return it from cache
4. The `refetch()` call might be short-circuited if React Query determines the cached data is still fresh

### Affected Code Locations:

1. **Test Helper**: `frontend/test/e2e/helpers/catalog-test-helpers.ts:327-345`
   - `waitForTableUpdate` function relies on API network request

2. **Wait Helper**: `frontend/test/e2e/helpers/wait-helpers.ts:98-109`
   - `waitForSearchResults` uses `page.waitForResponse()` which requires actual network activity

3. **Component**: `frontend/src/components/pages/CatalogList.tsx:139-157`
   - `handleClearFilters` calls `refetch()` but React Query may use cache

4. **API Hook**: `frontend/src/api/hooks/useCatalog.ts:145-162`
   - `useCatalogQuery` uses React Query with 5-minute staleTime

## Impact Assessment

### User-Facing Functionality Affected:
- **Actual functionality**: Likely working correctly - users can clear filters and see full dataset
- **Test reliability**: Test fails even though feature works
- **CI/CD impact**: False negative in E2E test suite

### Severity:
- **User Impact**: None - this appears to be a test-only issue
- **Test Suite Impact**: High - creates false failures in regression suite
- **Development Impact**: Moderate - developers may waste time investigating working feature

## Fix Proposal

### Recommended Approach: **Use UI-Based Waiting Instead of API Response**

The test helper has already been updated to use `waitForLoadingComplete()` which waits for UI loading indicators instead of API responses. However, the `clearAllFilters` helper still calls `waitForTableUpdate` which expects an API response.

### Code Changes Required:

#### Option 1: Update `clearAllFilters` helper to use UI-based waiting (RECOMMENDED)
**File**: `frontend/test/e2e/helpers/catalog-test-helpers.ts:147-159`

**Current implementation:**
```typescript
export async function clearAllFilters(page: Page): Promise<void> {
  console.log('🔄 Clearing all filters');
  const clearButton = getClearButton(page);
  await clearButton.click();

  // Wait for loading to complete (UI-based, more reliable than API response)
  await waitForLoadingComplete(page, { timeout: 30000 });

  // Small additional wait for table to stabilize
  await page.waitForTimeout(500);
  console.log('✅ All filters cleared');
}
```

**Analysis**: The helper is ALREADY using `waitForLoadingComplete` instead of `waitForTableUpdate`. This means the helper itself is correct!

**The issue is in the test itself** at line 247 of `clear-filters.spec.ts`:
```typescript
await clearButton.click();

await waitForTableUpdate(page);  // ⬅️ Should not call this explicitly!
```

The test is calling `waitForTableUpdate(page)` explicitly after the clear button click, but it should rely on the `clearAllFilters` helper which already includes proper waiting logic.

#### Fix: Update the test to use the helper consistently

**File**: `frontend/test/e2e/catalog/clear-filters.spec.ts:243-247`

**Change from:**
```typescript
// Clear filters
const clearButton = getClearButton(page);
await expect(clearButton).toBeVisible();
await clearButton.click();

await waitForTableUpdate(page);
```

**Change to:**
```typescript
// Clear filters
await clearAllFilters(page);
```

**Rationale:**
- The `clearAllFilters` helper already handles clicking the button and waiting for the UI to update
- The test is redundantly clicking the button directly and then trying to wait for an API response
- Using the helper provides consistent, UI-based waiting that doesn't depend on network requests

### Alternative Option 2: Force React Query to always make network request

Add `refetchOnMount: 'always'` to the query configuration, but this would impact performance and is not recommended.

### Estimated Complexity:
**Simple** - This is a one-line fix to use the existing helper consistently.

## Related Failures

This same pattern likely affects other timeout failures in the catalog module:

1. **TASK-002**: filter-edge-cases numbers in product name - Same `waitForTableUpdate` timeout pattern
2. **TASK-003**: filter-edge-cases hyphens and spaces - Same pattern
3. **TASK-004**: filter-edge-cases long product names - Same pattern
4. **TASK-005**: filter-edge-cases long product codes - Same pattern
5. **TASK-006**: filter-edge-cases single character search - Same pattern
6. **TASK-007**: filter-edge-cases numeric-only search - Same pattern
7. **TASK-008**: filter-edge-cases regex special characters - Same pattern
8. **TASK-010**: sorting-with-filters empty results - Same pattern
9. **TASK-011 to TASK-018**: text-search-filters tests - Same pattern

**Common theme**: All tests that explicitly call `waitForTableUpdate(page)` after using helpers that already include proper waiting.

### Systematic Fix Strategy:
1. Review all test files for explicit `waitForTableUpdate(page)` calls
2. Replace with appropriate helper functions that already include waiting
3. Ensure test helpers use UI-based waiting (`waitForLoadingComplete`) instead of API response waiting
4. Update test documentation to clarify when to use helpers vs manual waits

## Verification Plan

After implementing the fix:

1. **Run the specific test**:
   ```bash
   ./scripts/run-playwright-tests.sh catalog/clear-filters.spec.ts -g "should handle clearing filters from empty result state"
   ```

2. **Run all clear-filters tests**:
   ```bash
   ./scripts/run-playwright-tests.sh catalog/clear-filters.spec.ts
   ```

3. **Run all catalog module tests**:
   ```bash
   ./scripts/run-playwright-tests.sh catalog
   ```

4. **Verify against staging**:
   - Tests run against https://heblo.stg.anela.cz
   - Ensure proper authentication is used (navigateToApp)

## Additional Notes

### Pattern Discovery:
The test helpers were recently updated (based on the code) to use UI-based waiting (`waitForLoadingComplete`) instead of API response waiting (`waitForResponse`). However, many tests still call `waitForTableUpdate(page)` explicitly instead of relying on the helpers.

This suggests a broader pattern:
- **Old pattern**: Tests manually triggered actions and waited for API responses
- **New pattern**: Helper functions handle both actions and UI-based waiting
- **Gap**: Tests not fully migrated to use new helper pattern

### Recommendation for Future Tests:
1. **Always use helper functions** for common operations (filtering, clearing, sorting)
2. **Helpers should encapsulate waiting** - tests should not manually wait after using a helper
3. **Prefer UI-based waiting** over API response waiting to avoid React Query cache issues
4. **Document helper waiting behavior** so test authors know not to add redundant waits

### Related Documentation to Update:
- `docs/testing/playwright-e2e-testing.md` - Add guidance on using test helpers
- `frontend/test/e2e/helpers/README.md` - Create if doesn't exist, document helper waiting behavior
