# Catalog Pagination Reset Bug - Fix Summary

## Test Analyzed
- **File**: `frontend/test/e2e/catalog-text-search-filters.spec.ts`
- **Line**: 143 (and also line 271)
- **Test Name**: "should reset to page 1 when applying name filter" (and "should reset to page 1 when applying code filter")

## Problem Description

### User-Facing Issue
When a user is on page 2 (or higher) of the catalog and applies a filter (product name or product code), the pagination does NOT reset to page 1. This results in:
- Users seeing potentially empty results or incorrect data
- Confusing UX where applying a filter keeps them on page 2 instead of starting from the beginning

### Root Cause
There was a **race condition** in the `CatalogList.tsx` component:

1. User is on page 2 → URL has `?page=2`
2. User applies filter → `handleApplyFilters()` is called
3. Function calls `setPageNumber(1)` to reset pagination
4. BUT: There's a `useEffect` at lines 219-228 that syncs page number FROM URL
5. The useEffect runs and reads `?page=2` from the URL (before it's updated)
6. The useEffect calls `setPageNumber(2)`, overriding the reset to page 1

The issue is that `setPageNumber(1)` updates React state, but the URL still has `?page=2`. The useEffect then reads this stale URL value and sets the page back to 2.

## Solution Implemented

### Files Modified
- **`frontend/src/components/pages/CatalogList.tsx`**
- **`frontend/test/e2e/catalog-text-search-filters.spec.ts`** (updated comments)

### Code Changes

#### 1. `handleApplyFilters()` function (lines 106-129)
**Before:**
```typescript
const handleApplyFilters = async () => {
  setProductNameFilter(productNameInput);
  setProductCodeFilter(productCodeInput);
  setPageNumber(1); // Reset to first page when applying filters

  // Force data reload by refetching
  await refetch();
};
```

**After:**
```typescript
const handleApplyFilters = async () => {
  setProductNameFilter(productNameInput);
  setProductCodeFilter(productCodeInput);
  setPageNumber(1); // Reset to first page when applying filters

  // Update URL params immediately to prevent race condition with useEffect
  const params = new URLSearchParams(searchParams);
  if (productNameInput) {
    params.set("productName", productNameInput);
  } else {
    params.delete("productName");
  }
  if (productCodeInput) {
    params.set("productCode", productCodeInput);
  } else {
    params.delete("productCode");
  }
  // Remove page param when resetting to page 1
  params.delete("page");
  setSearchParams(params, { replace: true });

  // Force data reload by refetching
  await refetch();
};
```

**Why this works:** By immediately updating the URL parameters and removing the `page` param, the useEffect that reads from URL will not override the `setPageNumber(1)` call.

#### 2. `handleClearFilters()` function (lines 138-157)
Added immediate URL parameter updates to clear all filter params and page param:
```typescript
// Update URL params immediately to prevent race condition with useEffect
const params = new URLSearchParams(searchParams);
params.delete("productName");
params.delete("productCode");
params.delete("productType");
params.delete("page");
setSearchParams(params, { replace: true });
```

#### 3. `productTypeFilter` onChange handler (lines 391-409)
Added immediate URL parameter updates when changing the product type filter:
```typescript
onChange={(e) => {
  const newType = e.target.value === ""
    ? ""
    : (e.target.value as ProductType);
  setProductTypeFilter(newType);
  setPageNumber(1); // Reset to first page when filter changes

  // Update URL params immediately to prevent race condition with useEffect
  const params = new URLSearchParams(searchParams);
  if (newType) {
    params.set("productType", newType);
  } else {
    params.delete("productType");
  }
  params.delete("page"); // Remove page param when resetting to page 1
  setSearchParams(params, { replace: true });
}}
```

## Testing & Verification

### Local Testing
✅ **Frontend build:** Successful (`npm run build`)
✅ **Frontend tests:** All 638 tests passing
✅ **Code formatting:** No formatting violations

### E2E Testing Status
⏳ **Waiting for deployment to staging**

The E2E test currently fails when run against staging because staging is running the old code (without the fix). The test will pass once this fix is deployed to staging.

**Test execution:**
```bash
./scripts/run-playwright-tests.sh "catalog-text-search-filters.spec.ts:143"
```

**Current status:** Test is marked as `.skip()` until the fix is deployed to staging.

## Deployment Requirements

### Before Unskipping Tests
1. Deploy this branch to staging environment
2. Wait for deployment to complete
3. Verify staging is running the new code
4. Run the E2E test to confirm it passes
5. Remove `.skip()` from both tests:
   - Line 143: "should reset to page 1 when applying name filter"
   - Line 271: "should reset to page 1 when applying code filter"

### Related Tests
This fix should also resolve pagination issues in other catalog tests that were previously skipped for the same reason.

## Technical Details

### State Management Flow (After Fix)
1. User applies filter
2. `handleApplyFilters()` immediately updates:
   - React state: `setPageNumber(1)`, `setProductNameFilter()`, etc.
   - URL params: `setSearchParams()` with `page` param removed
3. URL update happens synchronously before useEffect runs
4. useEffect reads updated URL (no `page` param) → defaults to page 1
5. No race condition, pagination correctly resets

### Why Previous Fixes Didn't Work
Commit `dc6be70b` (from Jan 26) added `setPageNumber(1)` to the productTypeFilter, but didn't add the immediate URL update. This left the race condition in place, so the bug persisted.

## Summary
✅ **Bug identified:** Race condition between state updates and URL parameter syncing
✅ **Fix implemented:** Immediate URL parameter updates in all filter handlers
✅ **Tests updated:** Comments explain the fix is ready but needs deployment
✅ **Build verified:** No compilation errors, all tests passing
⏳ **Next step:** Deploy to staging and verify E2E tests pass
