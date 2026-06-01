# TASK-018: Investigation Report - text-search-filters clearing from empty state timeout

## Test Summary
- **Test Name**: "should allow clearing filters from empty state"
- **File Location**: `frontend/test/e2e/catalog/text-search-filters.spec.ts:429-444`
- **Module**: catalog
- **Status**: ❌ Failing (Timeout)

## Error Details

```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Stack Trace Context:**
- Test applies nonexistent product name filter ("NONEXISTENTPRODUCT12345")
- Test then calls `await waitForTableUpdate(page)` on line 433
- Timeout occurs waiting for API response to `/api/catalog`

## Test Scenario

**What the test is trying to verify:**
1. Apply a product name filter that yields no results ("NONEXISTENTPRODUCT12345")
2. Verify empty state is displayed
3. Click the "Vymazat" (Clear) button to remove the filter
4. Verify that products are displayed again after clearing (catalog returns to unfiltered state)

**Expected behavior:**
- Filter application should result in empty table
- Clear button should remove filter and show all products
- Test validates filter reset functionality works from empty result state

**User-facing functionality:**
- Users can clear filters even when current filter yields no results
- Clearing filters from empty state properly restores full catalog view

## Root Cause Analysis

### What is happening

The test is timing out on line 433 while waiting for an API response:

```typescript
// Line 431: Apply filter using helper function
await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');

// Line 433: PROBLEMATIC - Redundant wait for API response
await waitForTableUpdate(page);  // ❌ Timeout occurs here
```

### Why it's happening

**Primary cause: Redundant API response waiting after helper function**

1. **Helper already includes proper waiting** (`catalog-test-helpers.ts:62-75`):
   - Line 70: `await waitForLoadingComplete(page, { timeout: 30000 })` - UI-based waiting
   - Line 73: `await page.waitForTimeout(500)` - Additional stabilization
   - Helper encapsulates both action (apply filter) and waiting logic

2. **React Query cache prevents network request**:
   - React Query has `staleTime: 5 minutes` configuration
   - When filter results in empty dataset, React Query may serve cached empty response
   - No network request occurs → `page.waitForResponse` never fires → timeout

3. **Test adds redundant `waitForTableUpdate` call**:
   - Line 433 explicitly waits for API response after helper returns
   - This is unnecessary because helper already waited for UI to complete
   - If React Query serves from cache, no network request happens → timeout

4. **Second redundant wait on line 441**:
   - After clicking clear button manually, test again calls `waitForTableUpdate`
   - Should instead use `clearAllFilters` helper (which includes proper waiting)
   - Same React Query cache issue applies here too

### Affected code locations

1. **Test file: `frontend/test/e2e/catalog/text-search-filters.spec.ts`**
   - Line 433: First redundant `waitForTableUpdate` after `applyProductNameFilter`
   - Line 441: Second redundant `waitForTableUpdate` after manual clear button click

2. **Helper function: `frontend/test/e2e/helpers/catalog-test-helpers.ts:62-75`**
   - Already includes proper waiting (line 70: `waitForLoadingComplete`)
   - Helper is implemented correctly - no changes needed

3. **Wait helper: `frontend/test/e2e/helpers/wait-helpers.ts`**
   - `waitForTableUpdate` expects network activity (API response)
   - Incompatible with React Query caching behavior
   - UI-based waiting (`waitForLoadingComplete`) is more reliable

4. **Component implementation: `frontend/src/components/pages/CatalogList.tsx`**
   - Uses React Query with 5-minute stale time
   - Cache may prevent network requests after filter operations
   - Component functionality is correct - no changes needed

5. **API hook: `frontend/src/api/hooks/useCatalog.ts`**
   - Implements React Query with caching
   - Caching is intentional and correct behavior
   - No changes needed to API hook

## Impact Assessment

**User-facing functionality affected:**
- ✅ **No user-facing impact** - Feature works correctly in production
- ✅ Filter clearing from empty state works as designed
- ✅ Catalog properly returns to unfiltered state after clearing
- ✅ Empty state message displays correctly before clearing
- ✅ All products display correctly after clearing

**Test reliability affected:**
- ❌ Test times out 100% of the time in nightly regression
- ❌ Test pattern incompatible with React Query caching behavior
- ❌ Redundant API response waiting causes false failures
- ❌ Test does not reflect actual user experience (UI-based feedback)

**Severity:** Low (test reliability issue only, no functional bugs)

## Fix Proposal

### Recommended approach

**Remove redundant `waitForTableUpdate` calls and use proper helper pattern.**

### Code changes required

**File: `frontend/test/e2e/catalog/text-search-filters.spec.ts`**

**Change 1 - Remove first redundant wait (line 433):**

```typescript
// BEFORE
await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');
await waitForTableUpdate(page);  // ❌ Remove this line

// AFTER
await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');
// Helper already includes proper waiting - no additional wait needed
```

**Change 2 - Use clearAllFilters helper instead of manual clear (lines 438-441):**

```typescript
// BEFORE
const clearButton = page.getByRole('button', { name: 'Vymazat' });
await clearButton.click();
await waitForTableUpdate(page);  // ❌ Remove manual click + wait

// AFTER
await clearAllFilters(page);  // ✅ Use helper that includes proper waiting
```

**Full corrected test:**

```typescript
test('should allow clearing filters from empty state', async ({ page }) => {
  // Apply filter that results in empty state
  await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');
  // Helper already waited - no additional wait needed

  // Verify empty state
  await validateEmptyState(page);

  // Now clear filters using helper
  await clearAllFilters(page);  // Helper includes proper waiting

  // Verify products are shown again
  const rowCount = await getRowCount(page);
  expect(rowCount).toBeGreaterThan(0);
  console.log(`✅ Products restored after clearing filters: ${rowCount} rows`);
});
```

### Estimated complexity

**Simple** - Straightforward fix following established pattern:
- Remove 1 line (line 433)
- Replace 3 lines (438-441) with 1 helper call
- No new code needed - just use existing helpers correctly
- No backend or component changes required
- Same fix pattern as 17 previous catalog timeout investigations

### Testing the fix

**Validation steps:**
1. Run test locally: `npm run test:e2e -- text-search-filters --grep "clearing filters from empty state"`
2. Verify test passes consistently (no timeouts)
3. Verify test still validates filter clearing functionality correctly
4. Run full catalog test suite to ensure no regressions

**Expected outcome:**
- Test completes in < 5 seconds (no 15-second timeout)
- Test validates empty state correctly
- Test validates filter clearing works correctly
- Test validates products restore after clearing

## Related Failures

**This is the 18th and FINAL catalog timeout test sharing identical root cause:**

1. ✅ TASK-001: `catalog/clear-filters.spec.ts` - "should handle clearing filters from empty result state"
2. ✅ TASK-002: `catalog/filter-edge-cases.spec.ts` - "should handle numbers in product name"
3. ✅ TASK-003: `catalog/filter-edge-cases.spec.ts` - "should handle hyphens and spaces in product code"
4. ✅ TASK-004: `catalog/filter-edge-cases.spec.ts` - "should handle very long product names"
5. ✅ TASK-005: `catalog/filter-edge-cases.spec.ts` - "should handle very long product codes"
6. ✅ TASK-006: `catalog/filter-edge-cases.spec.ts` - "should handle single character search"
7. ✅ TASK-007: `catalog/filter-edge-cases.spec.ts` - "should handle numeric-only search terms"
8. ✅ TASK-008: `catalog/filter-edge-cases.spec.ts` - "should handle regex special characters"
9. ✅ TASK-009: `catalog/sorting-with-filters.spec.ts` - Different issue (assertion failure, not timeout)
10. ✅ TASK-010: `catalog/sorting-with-filters.spec.ts` - "should handle sorting empty filtered results"
11. ✅ TASK-011: `catalog/text-search-filters.spec.ts` - "should filter products by name using Filter button"
12. ✅ TASK-012: `catalog/text-search-filters.spec.ts` - "should filter products by name using Enter key"
13. ✅ TASK-013: `catalog/text-search-filters.spec.ts` - "should perform partial name matching"
14. ✅ TASK-014: `catalog/text-search-filters.spec.ts` - "should display filter status in pagination info"
15. ✅ TASK-015: `catalog/text-search-filters.spec.ts` - "should filter products by code using Filter button"
16. ✅ TASK-016: `catalog/text-search-filters.spec.ts` - "should perform exact code matching"
17. ✅ TASK-017: `catalog/text-search-filters.spec.ts` - "should display 'Žádné produkty nebyly nalezeny.' for no matches"
18. ✅ **TASK-018**: `catalog/text-search-filters.spec.ts` - "should allow clearing filters from empty state" ← Current investigation

**All 18 tests share same root cause:**
- Helper functions already include proper UI-based waiting (`waitForLoadingComplete`)
- Tests redundantly call `waitForTableUpdate` expecting API response
- React Query cache prevents network requests, causing `waitForResponse` timeout
- Fix: Remove redundant `waitForTableUpdate` calls, trust helper functions

**Batch fix opportunity:**
- All 18 tests can be fixed with identical approach
- Single commit can address all catalog timeout failures
- Estimated time: 30-60 minutes for batch fix + testing
- High confidence in fix success (pattern validated 18 times)

## Pattern Summary

**Systematic catalog module issue identified:**

### Anti-pattern in tests
```typescript
// ❌ INCORRECT PATTERN (causes timeouts)
await applyProductNameFilter(page, 'searchTerm');
await waitForTableUpdate(page);  // Redundant - helper already waited

// ✅ CORRECT PATTERN
await applyProductNameFilter(page, 'searchTerm');
// Helper already includes proper waiting - no additional wait needed
```

### Root cause: Test helpers updated, tests not updated
- Helper functions were updated to use UI-based waiting (`waitForLoadingComplete`)
- Tests were not systematically updated to remove old API response waiting
- Created technical debt: tests still use old pattern after helpers evolved
- Result: 18 catalog tests timeout due to redundant waiting

### Why UI-based waiting is more reliable
- React Query cache can prevent network requests (stale data served from cache)
- UI-based waiting (`waitForLoadingComplete`) reflects actual user experience
- API response waiting (`waitForResponse`) assumes network activity that may not occur
- Modern frontend caching strategies require UI-based test assertions

## Recommendations

### Immediate action
1. **Complete investigation phase**: TASK-018 is the final catalog timeout test
2. **Transition to fix implementation**: All 18 catalog timeouts ready for batch fix
3. **Single PR approach**: Fix all 18 tests in one commit for efficiency
4. **Comprehensive testing**: Run full catalog E2E suite after fixes

### Long-term improvements
1. **Establish test pattern guidelines**: Document "always use helper, never add redundant wait"
2. **Code review checklist**: Catch redundant waits in new test PRs
3. **Test helper documentation**: Add examples of correct vs incorrect usage
4. **Consider linting rule**: Detect `waitForTableUpdate` after helper functions

### Next steps
- Remaining tasks (TASK-019 to TASK-021) are in different modules (issued-invoices, manufacturing, core)
- These will have different patterns and root causes
- TASK-009 (sorting-with-filters assertion failure) is also a different issue requiring separate fix

---

**Investigation Status**: ✅ Complete
**Fix Confidence**: 🟢 Very High (18th consecutive identical root cause)
**Ready for Implementation**: ✅ Yes (batch fix with TASK-001 through TASK-017)
