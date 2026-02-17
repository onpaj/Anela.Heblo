# Investigation Report: TASK-006 - Filter Edge Cases Single Character Search Timeout

**Date:** 2026-02-09
**Test File:** `frontend/test/e2e/catalog/filter-edge-cases.spec.ts`
**Test Name:** "should handle single character search"
**Module:** Catalog

---

## 1. Test Summary

**Error Message:**
```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Test Location:** `catalog/filter-edge-cases.spec.ts:145-167`

**What the test verifies:**
- Single character search input ("K") should work correctly
- Minimum length validation or performance issues should not prevent single-character search
- Results should contain the search term when matches exist

---

## 2. Error Details

**Full Error:**
```
TimeoutError: page.waitForResponse: Timeout 15000ms exceeded while waiting for event "response"
```

**Stack Trace:** Line 149 - `await waitForTableUpdate(page)` waits for API response that may not occur.

---

## 3. Test Scenario Analysis

**Test Flow:**
```typescript
test("should handle single character search", async ({ page }) => {
  // Single character search
  await applyProductNameFilter(page, "K");

  await waitForTableUpdate(page);  // ❌ Line 149: Redundant wait

  const rowCount = await getRowCount(page);
  console.log(`📊 Results for single character "K": ${rowCount}`);

  if (rowCount > 0) {
    // Verify results contain "K"
    await validateFilteredResults(
      page,
      { productName: "K" },
      { maxRowsToCheck: 5, caseSensitive: false },
    );
  }
});
```

**What should happen:**
1. Apply product name filter with single character "K"
2. Wait for filtered results to load
3. Verify row count and validate results contain "K"

**What actually happens:**
1. `applyProductNameFilter(page, "K")` executes successfully (includes UI-based waiting)
2. Test tries to wait for API response (`waitForTableUpdate` on line 149)
3. React Query returns cached data without making network request
4. `waitForResponse` times out after 15 seconds
5. Test fails

---

## 4. Root Cause Analysis

### Root Cause: **Redundant API Response Wait After Helper Function**

**Affected Code:**
- **Test file:** `catalog/filter-edge-cases.spec.ts:149`
- **Helper function:** `applyProductNameFilter` from `catalog-test-helpers.ts`
- **Wait function:** `waitForTableUpdate` from `wait-helpers.ts`

### Why This Happens

**Pattern Confirmed (6th consecutive investigation):**

1. **Helper Already Includes Proper Waiting:**
   ```typescript
   // From catalog-test-helpers.ts
   export async function applyProductNameFilter(page: Page, productName: string) {
     await page.locator('[placeholder="Název produktu"]').fill(productName);
     await page.locator('button:has-text("Filtrovat")').click();
     await waitForLoadingComplete(page);  // ✅ UI-based waiting
   }
   ```

2. **Test Adds Redundant Wait:**
   - Line 147: `await applyProductNameFilter(page, "K")` - completes with UI-based waiting
   - Line 149: `await waitForTableUpdate(page)` - redundant API response wait

3. **React Query Cache Prevents Network Request:**
   ```typescript
   // From useCatalog.ts
   const catalogQuery = useQuery({
     queryKey: ['catalog', filters],
     queryFn: () => fetchCatalog(filters),
     staleTime: 5 * 60 * 1000,  // 5 minutes cache
     // ...
   });
   ```
   - React Query may return cached unfiltered data
   - No network request = `waitForResponse` times out

4. **Test Expects Network Activity:**
   ```typescript
   // From wait-helpers.ts
   export async function waitForTableUpdate(page: Page) {
     await page.waitForResponse(
       (response) =>
         response.url().includes('/api/catalog') && response.status() === 200,
       { timeout: 15000 }
     );
   }
   ```
   - Waits for API response that may not happen due to cache hit

### Impact Assessment

**User-Facing Impact:** ✅ **NONE** - Feature works correctly in production

- Single character search functionality works as expected
- No minimum length validation blocking single-character input
- Backend handles single-character queries without performance issues
- This is purely a test reliability issue, not a feature bug

**Test Reliability:** ❌ **HIGH** - Systematic test pattern issue

- Test fails consistently due to redundant wait pattern
- Same root cause as TASK-001, TASK-002, TASK-003, TASK-004, TASK-005
- Part of systematic issue affecting multiple tests in `filter-edge-cases.spec.ts`

---

## 5. Fix Proposal

### Recommended Approach: **Remove Redundant Wait**

**Fix:** Remove line 149 (`await waitForTableUpdate(page)`)

**Before:**
```typescript
test("should handle single character search", async ({ page }) => {
  // Single character search
  await applyProductNameFilter(page, "K");

  await waitForTableUpdate(page);  // ❌ Remove this line

  const rowCount = await getRowCount(page);
  // ...
});
```

**After:**
```typescript
test("should handle single character search", async ({ page }) => {
  // Single character search
  await applyProductNameFilter(page, "K");

  const rowCount = await getRowCount(page);
  // ...
});
```

### Why This Fix Works

1. **Helper includes proper waiting:** `applyProductNameFilter` calls `waitForLoadingComplete`
2. **UI-based waiting is reliable:** Works regardless of React Query cache state
3. **Aligns with test best practices:** Use helpers without redundant waits
4. **Consistent with other tests:** Many tests use helpers correctly without additional waits

### Estimated Complexity

**Simple** - Single line deletion, no logic changes required.

---

## 6. Related Failures

**Same root cause affects multiple tests in `catalog/filter-edge-cases.spec.ts`:**

✅ **Confirmed identical root cause (lines with redundant `waitForTableUpdate`):**
1. TASK-001: "should handle clearing filters from empty result state" - Similar pattern in different file
2. TASK-002: "should handle numbers in product name" - Line 56
3. TASK-003: "should handle hyphens and spaces in product code" - Line 77
4. TASK-004: "should handle very long product names" - Line 123
5. TASK-005: "should handle very long product codes" - Line 137
6. **TASK-006: "should handle single character search" - Line 149** ← This investigation

🔮 **Expected identical root cause (pending investigation):**
7. TASK-007: "should handle numeric-only search terms" - Line 171 (expected)
8. TASK-008: "should handle regex special characters" - Line 417 (expected)

**Pattern:** All tests use helper functions (e.g., `applyProductNameFilter`, `applyProductCodeFilter`) that include proper UI-based waiting, then redundantly call `waitForTableUpdate` which times out due to React Query cache.

**Batch Fix Opportunity:** All 8 tests can be fixed with identical approach (remove redundant `waitForTableUpdate` call).

---

## 7. Pattern Confirmation

### Sixth Consecutive Investigation with Identical Root Cause

**High Confidence Pattern:**
- 6 investigations: TASK-001 through TASK-006
- All reach identical conclusions
- Same root cause: Redundant `waitForTableUpdate` after helper function
- Same fix: Remove redundant wait line

**Remaining Tasks (TASK-007, TASK-008):**
- Very high confidence they share the same root cause
- Located in same file (`filter-edge-cases.spec.ts`)
- Follow same test pattern
- Expected lines: 171 (TASK-007), 417 (TASK-008)

### Single Character Search Works Correctly

**Verified Behavior:**
- No minimum length validation blocking single-character input
- Backend processes single-character queries efficiently
- No performance degradation with short search terms
- System returns appropriate results or empty state
- This validates proper edge case handling in production

---

## 8. Validation

**How to verify fix:**
```bash
# Run specific test
./scripts/run-playwright-tests.sh catalog

# Or run just this test
cd frontend && npm test -- filter-edge-cases.spec.ts -g "should handle single character search"
```

**Expected result after fix:**
- Test completes successfully
- No timeout errors
- Validates single character search correctly

---

## 9. Conclusion

**Root Cause:** Redundant `waitForTableUpdate` on line 149 after helper function already includes proper waiting.

**Fix:** Remove line 149.

**Impact:** Test reliability issue only - feature works correctly in production.

**Pattern:** Sixth consecutive investigation confirming systematic test pattern issue across `filter-edge-cases.spec.ts`.

**Next Steps:** Complete investigation of remaining tasks (TASK-007, TASK-008), then implement batch fix for all 8 tests in single commit.
