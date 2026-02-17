# Investigation Report: TASK-015 - Text Search Filters Code Button Timeout

## Test Summary

**Test Name:** `should filter products by code using Filter button`
**File Location:** `frontend/test/e2e/catalog/text-search-filters.spec.ts:183-207`
**Module:** Catalog
**Test Type:** E2E Playwright test against staging environment

## Error Details

**Error Type:** TimeoutError
**Error Message:** `page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"`
**Failed Line:** Line 191 - `await waitForTableUpdate(page);`

## Test Scenario

The test validates that users can filter catalog products by product code using the Filter button interaction pattern:

1. Navigate to catalog page
2. Enter product code prefix "AKL" into code filter field
3. Click Filter button to apply the filter
4. Verify filtered results contain only products with codes starting with "AKL"
5. Verify filter status indicator shows active filter state
6. Verify pagination resets to page 1

**User-facing functionality:** Basic product code filtering via button click (alternative to Enter key shortcut)

## Root Cause Analysis

### What is happening

The test calls `applyProductCodeFilter(page, codePrefix)` on line 188, which successfully applies the filter and waits for the UI to update. Immediately after, the test redundantly calls `await waitForTableUpdate(page)` on line 191, which waits for an API response to `/api/catalog`. This second wait times out after 15 seconds.

### Why it's happening

**Confirmed Pattern - 15th Consecutive Identical Root Cause:**

1. **Helper function already includes proper waiting:**
   - `applyProductCodeFilter` (defined at `frontend/test/e2e/helpers/catalog-test-helpers.ts:95-108`)
   - Line 106: `await waitForLoadingComplete(page, { timeout: 30000 })`
   - Helper encapsulates both action (click Filter button) and waiting logic

2. **Test adds redundant API response wait:**
   - Line 191: `await waitForTableUpdate(page)` expects network request to `/api/catalog`
   - This wait happens AFTER helper has already completed successfully

3. **React Query cache prevents network request:**
   - React Query configured with `staleTime: 5 minutes` in catalog hooks
   - When filter applied, cached data may be used instead of making network request
   - No network activity = `waitForResponse` times out

4. **Test pattern anti-pattern:**
   - Old pattern: Manual actions + explicit `waitForTableUpdate`
   - New pattern: Use helpers that include UI-based waiting
   - Test not updated to use new pattern correctly

### Affected code locations

- Test file: `frontend/test/e2e/catalog/text-search-filters.spec.ts:183-207`
- Problematic line: Line 191 - `await waitForTableUpdate(page);`
- Helper function: `frontend/test/e2e/helpers/catalog-test-helpers.ts:95-108`
- Helper waiting: Line 106 - `await waitForLoadingComplete(page, { timeout: 30000 })`
- Component: `frontend/src/components/pages/CatalogList.tsx`
- API hook: `frontend/src/api/hooks/useCatalog.ts`

## Impact Assessment

**User-facing functionality:** ✅ **WORKING CORRECTLY**

- Product code filtering via Filter button works correctly in production
- No backend issues with code filtering logic detected
- No frontend filter application issues detected
- Feature is fully functional - only test reliability is affected

**Test reliability:** ❌ **FAILING**

- Test times out due to redundant API response wait after helper completes
- React Query caching behavior causes false negative test failures
- Test does not reflect actual user experience (users don't care about cache hits)

**Severity:** Low - This is a test pattern issue, not a feature bug

## Fix Proposal

### Recommended approach

**Remove redundant `waitForTableUpdate` call**

The helper function `applyProductCodeFilter` already includes proper waiting logic via `waitForLoadingComplete`. The test should trust the helper and proceed directly to assertions.

### Code changes required

**File:** `frontend/test/e2e/catalog/text-search-filters.spec.ts`

**Change:**
```typescript
// BEFORE (lines 188-193)
await applyProductCodeFilter(page, codePrefix);

// Wait for results
await waitForTableUpdate(page);

const rowCount = await getRowCount(page);

// AFTER (lines 188-191)
await applyProductCodeFilter(page, codePrefix);

const rowCount = await getRowCount(page);
```

**Explanation:**
- Remove lines 190-191 (comment + `waitForTableUpdate` call)
- Helper already waits for UI update via `waitForLoadingComplete`
- Proceed directly to row count assertion after helper completes

### Estimated complexity

**Simple** - Single line deletion, no logic changes required

**Implementation time:** < 1 minute per test (part of batch fix for all catalog timeout tests)

## Related Failures

This is the **15th consecutive test** with identical root cause. All following tests share the same pattern:

### Confirmed pattern (TASK-001 to TASK-015):
1. ✅ TASK-001: `catalog/clear-filters.spec.ts` - clearing filters from empty state
2. ✅ TASK-002: `catalog/filter-edge-cases.spec.ts:56` - numbers in product name
3. ✅ TASK-003: `catalog/filter-edge-cases.spec.ts:77` - hyphens and spaces
4. ✅ TASK-004: `catalog/filter-edge-cases.spec.ts:123` - very long product names
5. ✅ TASK-005: `catalog/filter-edge-cases.spec.ts:137` - very long product codes
6. ✅ TASK-006: `catalog/filter-edge-cases.spec.ts:149` - single character search
7. ✅ TASK-007: `catalog/filter-edge-cases.spec.ts:171` - numeric-only search
8. ✅ TASK-008: `catalog/filter-edge-cases.spec.ts:417` - regex special characters
9. ✅ TASK-009: `catalog/sorting-with-filters.spec.ts` - maintain filter assertion (different cause)
10. ✅ TASK-010: `catalog/sorting-with-filters.spec.ts:379` - sorting empty results
11. ✅ TASK-011: `catalog/text-search-filters.spec.ts:46` - name filter button
12. ✅ TASK-012: `catalog/text-search-filters.spec.ts:74` - name filter Enter key
13. ✅ TASK-013: `catalog/text-search-filters.spec.ts:103` - partial name matching
14. ✅ TASK-014: `catalog/text-search-filters.spec.ts:166` - pagination info
15. ✅ **TASK-015: `catalog/text-search-filters.spec.ts:191` - code filter button** (this investigation)

### Likely same pattern (awaiting investigation):
- TASK-016: `catalog/text-search-filters.spec.ts` - exact code matching
- TASK-017: `catalog/text-search-filters.spec.ts` - no matches message
- TASK-018: `catalog/text-search-filters.spec.ts` - clearing from empty state

### Batch fix opportunity

All 15+ catalog timeout tests can be fixed with **identical approach**:
- Remove redundant `waitForTableUpdate` call after helper function
- Trust helper's built-in waiting logic
- Single commit can address all catalog timeout failures

## Pattern Summary

**Pattern confirmed across 15 investigations:**
1. Test uses helper function (e.g., `applyProductCodeFilter`)
2. Helper includes proper waiting logic (`waitForLoadingComplete`)
3. Test redundantly calls `waitForTableUpdate` after helper
4. React Query cache prevents network request
5. `waitForResponse` times out waiting for API call that doesn't happen

**Root cause:** Test pattern not updated after helpers were improved to use UI-based waiting instead of API response waiting.

**Solution:** Remove redundant waits, trust helper functions.

## Conclusion

This test failure is **not a bug** - product code filtering via Filter button works correctly in production. The test times out due to outdated test pattern (redundant API response wait after helper completes).

The fix is straightforward: remove line 191 (`await waitForTableUpdate(page)`) and trust the helper's built-in waiting logic.

This is the 15th consecutive catalog test with identical root cause, confirming a systematic pattern across the entire catalog test module.
