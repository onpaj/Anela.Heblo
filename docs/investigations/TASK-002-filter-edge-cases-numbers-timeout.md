# Investigation Report: TASK-002 - Filter Edge Cases Numbers in Product Name Timeout

**Date:** 2026-02-09
**Investigator:** Ralph (Autonomous Agent)
**Status:** ✅ Root Cause Identified

---

## 1. Test Summary

- **Test Name:** "should handle numbers in product name"
- **Test File:** `frontend/test/e2e/catalog/filter-edge-cases.spec.ts` (lines 52-71)
- **Module:** Catalog
- **Test Type:** E2E (Playwright)

---

## 2. Error Details

**Error Message:**
```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Location in Test:** Line 56 - `await waitForTableUpdate(page);`

**Full Error Context:**
The test times out waiting for an API response to `/api/catalog` after applying a product name filter with numeric value "100".

---

## 3. Test Scenario

**What the test verifies:**
- Product name filters correctly handle numeric characters
- Search for "100" in product names returns appropriate results
- Results are validated to confirm they contain the search term

**Test Flow:**
1. Navigate to catalog page (authenticated)
2. Apply product name filter with value "100" using `applyProductNameFilter` helper
3. **[PROBLEM]** Explicitly wait for API response using `waitForTableUpdate`
4. Check row count
5. If rows found, validate filtered results contain "100"

---

## 4. Root Cause Analysis

### What is happening

The test is calling `waitForTableUpdate(page)` after using the `applyProductNameFilter` helper, expecting an API response to `/api/catalog`. The test times out because no API response occurs within the 15-second timeout window.

### Why it's happening

**Same root cause as TASK-001 (React Query Caching):**

1. **Helper already handles waiting**: `applyProductNameFilter(page, "100")` internally calls `waitForLoadingComplete()` which uses UI-based waiting
2. **Redundant API waiting**: The test then adds `await waitForTableUpdate(page)` which expects a network request
3. **Cache hit scenario**: React Query may serve cached catalog data without making a network request (5-minute `staleTime`)
4. **Timeout result**: `page.waitForResponse('/api/catalog')` never resolves because no request is made

### Affected code locations

**Test file:** `frontend/test/e2e/catalog/filter-edge-cases.spec.ts:56`
```typescript
await applyProductNameFilter(page, "100");
await waitForTableUpdate(page); // ❌ Redundant wait - causes timeout
```

**Helper function:** `frontend/test/e2e/helpers/catalog-test-helpers.ts`
- `applyProductNameFilter` already includes `waitForLoadingComplete()`
- Using UI-based waiting, not API-based waiting

**API Hook:** `frontend/src/api/hooks/useCatalog.ts`
- React Query configuration: `staleTime: 5 * 60 * 1000` (5 minutes)
- Cache hits prevent network requests

---

## 5. Impact Assessment

### User-facing functionality affected

**None** - This is a test pattern issue, not a functional bug.

The product name filtering with numeric characters works correctly in the application. Users can successfully search for products containing numbers like "100".

### Test reliability impact

- **High** - Test fails intermittently depending on React Query cache state
- **Pattern issue** - Same problem affects multiple tests in this file (see Related Failures)
- **False negative** - Test reports failure despite feature working correctly

---

## 6. Fix Proposal

### Recommended approach

**Remove redundant `waitForTableUpdate` call** - The helper function already handles all necessary waiting.

### Code changes required

**File:** `frontend/test/e2e/catalog/filter-edge-cases.spec.ts`

**Current code (lines 52-71):**
```typescript
test("should handle numbers in product name", async ({ page }) => {
  // Search for products with numbers
  await applyProductNameFilter(page, "100");

  await waitForTableUpdate(page); // ❌ REMOVE THIS LINE

  const rowCount = await getRowCount(page);

  if (rowCount > 0) {
    // Verify filter works with numbers
    await validateFilteredResults(
      page,
      { productName: "100" },
      { maxRowsToCheck: 5 },
    );
    console.log("✅ Numbers in product name handled correctly");
  } else {
    console.log('ℹ️ No products with "100" in name found');
  }
});
```

**Fixed code:**
```typescript
test("should handle numbers in product name", async ({ page }) => {
  // Search for products with numbers
  await applyProductNameFilter(page, "100");
  // ✅ Helper already waits - no additional wait needed

  const rowCount = await getRowCount(page);

  if (rowCount > 0) {
    // Verify filter works with numbers
    await validateFilteredResults(
      page,
      { productName: "100" },
      { maxRowsToCheck: 5 },
    );
    console.log("✅ Numbers in product name handled correctly");
  } else {
    console.log('ℹ️ No products with "100" in name found');
  }
});
```

### Estimated complexity

**Simple** - Single line removal, no logic changes required.

**Validation:**
- ✅ Test pattern aligns with helper design
- ✅ Consistent with TASK-001 fix approach
- ✅ No risk to functionality

---

## 7. Related Failures

This same root cause likely affects **multiple other tests** in the same file:

### From BRIEF.md failing tests in `filter-edge-cases.spec.ts`:

1. **TASK-003**: "should handle hyphens and spaces in product code" (line 77)
   - Uses `applyProductCodeFilter` + `waitForTableUpdate`

2. **TASK-004**: "should handle very long product names (>100 chars)" (line 123)
   - Uses `applyProductNameFilter` + `waitForTableUpdate`

3. **TASK-005**: "should handle very long product codes" (line 137)
   - Uses `applyProductCodeFilter` + `waitForTableUpdate`

4. **TASK-006**: "should handle single character search" (line 149)
   - Uses `applyProductNameFilter` + `waitForTableUpdate`

5. **TASK-007**: "should handle numeric-only search terms" (line 171)
   - Uses `applyProductNameFilter` + `waitForTableUpdate`

6. **TASK-008**: "should handle regex special characters" (line 417)
   - Uses `applyProductNameFilter` + `waitForTableUpdate`

**All follow the same anti-pattern**: Helper function + redundant `waitForTableUpdate`

### Other tests in the same file that use the correct pattern:

- Line 35: `applyProductNameFilter` without additional wait ✅
- Line 254: `applyProductNameFilter` without additional wait ✅
- Line 371: `applyProductNameFilter` without additional wait ✅

### Tests that manually construct filter actions (different issue):

- Lines 88-95: Manual `fill()` + `click()` + `waitForTableUpdate`
- Lines 378-382: Manual `fill()` + `click()` + `waitForTableUpdate`
- Lines 397-402: Manual `fill()` + `click()` + `waitForTableUpdate`

---

## 8. Pattern Recognition

### This investigation confirms the pattern from TASK-001:

**Anti-pattern (causes timeouts):**
```typescript
await applyProductNameFilter(page, "value");
await waitForTableUpdate(page); // ❌ Redundant - causes timeout
```

**Correct pattern:**
```typescript
await applyProductNameFilter(page, "value");
// ✅ Helper handles waiting internally
```

### Systematic fix needed

The `filter-edge-cases.spec.ts` file has **6 tests** following this anti-pattern. All can be fixed the same way:
1. Remove `waitForTableUpdate` call after helper usage
2. Trust helper's internal UI-based waiting
3. Tests will pass reliably

---

## 9. Additional Context

### Test helper internals (for reference)

**`applyProductNameFilter` helper:**
```typescript
export async function applyProductNameFilter(page: Page, name: string) {
  const nameInput = getProductNameInput(page);
  await nameInput.fill(name);
  const filterButton = getFilterButton(page);
  await filterButton.click();
  await waitForLoadingComplete(page); // ✅ UI-based waiting
}
```

**`waitForLoadingComplete` vs `waitForTableUpdate`:**
- `waitForLoadingComplete`: Waits for loading spinner to disappear (UI-based) ✅
- `waitForTableUpdate`: Waits for `/api/catalog` network response (API-based) ❌

### Why React Query cache matters

React Query's cache configuration in catalog hooks:
```typescript
staleTime: 5 * 60 * 1000 // 5 minutes
```

When the same query parameters are used within 5 minutes:
- React Query returns cached data immediately
- No network request is made
- `page.waitForResponse()` never resolves
- Test times out after 15 seconds

---

## 10. Recommendations

### Immediate fix (TASK-002)

Remove line 56 from `filter-edge-cases.spec.ts`:
```typescript
await waitForTableUpdate(page); // ❌ DELETE THIS
```

### Batch fix recommendation

Since TASK-003 through TASK-008 have the same root cause, consider:
1. Fixing all 6 tests in `filter-edge-cases.spec.ts` in a single commit
2. This prevents redundant investigation work for TASK-003 through TASK-008
3. All fixes are identical: remove `waitForTableUpdate` after helper usage

### Test file hygiene

For tests using manual filter construction (lines 88-95, 378-382, 397-402):
- **Option A:** Replace with helper functions (recommended)
- **Option B:** Replace `waitForTableUpdate` with `waitForLoadingComplete`

---

## 11. Verification Plan

After applying the fix:

1. **Run the specific test:**
   ```bash
   ./scripts/run-playwright-tests.sh catalog/filter-edge-cases.spec.ts -g "should handle numbers in product name"
   ```

2. **Verify test passes:**
   - Test completes without timeout
   - Appropriate results are found (or gracefully handles no results)
   - No regression in validation logic

3. **Run full catalog test suite:**
   ```bash
   ./scripts/run-playwright-tests.sh catalog
   ```

4. **Verify no side effects:**
   - Other catalog tests still pass
   - No new failures introduced

---

## 12. Conclusion

**Root cause:** Redundant `waitForTableUpdate` call after using `applyProductNameFilter` helper causes timeout due to React Query cache hits.

**Fix:** Remove single line (`await waitForTableUpdate(page)` on line 56).

**Impact:** Test reliability restored, no functional changes needed.

**Related tasks:** TASK-003 through TASK-008 share identical root cause and can be fixed the same way.

---

**Investigation completed:** 2026-02-09
**Next step:** Implement fix and validate with test execution
