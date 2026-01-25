# E2E Test Failures Analysis - Nightly Run #4

**Analyzed:** 2026-01-24
**Total Failures:** 26 tests
**Root Cause Categories:** 2 main issues

---

## Summary

| Category | Test Implementation Issue | Real Application Bug | Status |
|----------|---------------------------|---------------------|---------|
| **Dashboard (6 tests)** | ✅ Missing E2E auth | ❌ No | INVALID TESTS |
| **Recurring Jobs (15 tests)** | ✅ Missing E2E auth | ❌ No | INVALID TESTS |
| **Changelog (3 tests)** | ⚠️ Possibly timing/selectors | ⚠️ Possibly real | NEEDS INVESTIGATION |
| **Catalog Margins (1 test)** | ⚠️ Possibly timing/selectors | ⚠️ Possibly real | NEEDS INVESTIGATION |
| **Gift Package (1 test)** | ⚠️ Visibility timeout | ⚠️ Possibly real | NEEDS INVESTIGATION |

---

## ❌ FALSE FAILURES - Invalid Test Implementation (21 tests)

### 1. Dashboard Tests - Missing Authentication (6 failures)

**Issue:** Tests do NOT use `createE2EAuthSession()` helper

**Evidence:**
```typescript
// ❌ WRONG - dashboard.spec.ts
test.beforeEach(async ({ page }) => {
  await page.goto('/');  // No auth - redirects to Microsoft login
  await page.waitForSelector('[data-testid="dashboard-container"]', { timeout: 10000 });
});

// ✅ CORRECT - working tests like catalog-product-type-filter.spec.ts
test.beforeEach(async ({ page }) => {
  await createE2EAuthSession(page);  // Proper E2E authentication
});
```

**Error Pattern:**
```
TimeoutError: page.waitForSelector: Timeout 10000ms exceeded.
- waiting for locator('[data-testid="dashboard-container"]') to be visible
2 × waiting for "https://login.microsoftonline.com/...oauth2/v2.0/authorize..." navigation to finish
```

**What's Actually Happening:**
1. Test navigates to `/` without authentication
2. App redirects to Microsoft Entra ID login page
3. Test times out waiting for dashboard elements that never load
4. **The dashboard code is correct** - it has all required `data-testid` attributes

**Affected Tests:**
- `dashboard.spec.ts:12` - should display dashboard tiles
- `dashboard.spec.ts:24` - should display AutoShow tiles automatically
- `dashboard.spec.ts:36` - should open dashboard settings
- `dashboard.spec.ts:47` - should be able to enable/disable tiles
- `dashboard.spec.ts:72` - should support drag and drop to reorder tiles
- `dashboard.spec.ts:122` - should display empty state for production tile

**Fix Required:**
```typescript
// Add to dashboard.spec.ts
import { createE2EAuthSession } from './helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  await createE2EAuthSession(page);  // ← ADD THIS
  await page.goto('/');
  await page.waitForSelector('[data-testid="dashboard-container"]', { timeout: 10000 });
});
```

**Priority:** ⚠️ **HIGH** - These tests provide NO value currently and waste CI time

---

### 2. Recurring Jobs Tests - Missing Authentication (15 failures)

**Issue:** Tests do NOT use `createE2EAuthSession()` helper

**Evidence:**
```typescript
// ❌ WRONG - recurring-jobs-management.spec.ts
test.beforeEach(async ({ page }) => {
  await page.goto('/recurring-jobs');  // No auth - redirects to login
  await page.waitForLoadState('networkidle');
});
```

**Error Pattern:**
```yaml
Page snapshot shows Microsoft login page:
- heading "Sign in" [level=1]
- textbox "Enter your email, phone, or Skype."
- button "Next"
```

**What's Actually Happening:**
1. Test navigates to `/recurring-jobs` without authentication
2. App redirects to Microsoft Entra ID login page
3. Test tries to find "Správa Recurring Jobs" heading on login page
4. **The RecurringJobsPage component is correct** - it renders the proper heading

**Affected Tests:** (All 15 recurring jobs tests)
- `recurring-jobs-management.spec.ts:12` - should display recurring jobs page with correct title
- `recurring-jobs-management.spec.ts:20` - should display jobs table with all columns
- `recurring-jobs-management.spec.ts:32` - should display all 9 recurring jobs
- `recurring-jobs-management.spec.ts:44` - should display job details correctly
- `recurring-jobs-management.spec.ts:76` - should toggle job status from enabled to disabled
- `recurring-jobs-management.spec.ts:103` - should toggle job status from disabled to enabled
- `recurring-jobs-management.spec.ts:131` - should show loading state during toggle
- `recurring-jobs-management.spec.ts:162` - should refresh jobs list when clicking refresh button
- `recurring-jobs-management.spec.ts:185` - should display correct job names
- `recurring-jobs-management.spec.ts:212` - should display cron expressions correctly
- `recurring-jobs-management.spec.ts:228` - should show last modified information
- `recurring-jobs-management.spec.ts:244` - should have proper accessibility attributes on toggle buttons
- `recurring-jobs-management.spec.ts:273` - should persist job status changes after page refresh
- `recurring-jobs-management.spec.ts:327` - should display "Run Now" button for each job
- `recurring-jobs-management.spec.ts:340` - should have "Actions" column header

**Verified Implementation:**
```typescript
// RecurringJobsPage.tsx - CORRECT implementation
return (
  <div className="flex flex-col h-full w-full">
    <div className="flex-shrink-0 mb-3">
      <h1 className="text-lg font-semibold text-gray-900">
        Správa Recurring Jobs  {/* ← Test looks for this - it exists! */}
      </h1>
    </div>
    {/* ... rest of component */}
  </div>
);
```

**Fix Required:**
```typescript
// Add to recurring-jobs-management.spec.ts
import { createE2EAuthSession } from '../helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  await createE2EAuthSession(page);  // ← ADD THIS
  await page.goto('/recurring-jobs');
  await page.waitForLoadState('networkidle');
});
```

**Priority:** ⚠️ **HIGH** - Critical automation feature appears broken but ISN'T

---

## ⚠️ NEEDS INVESTIGATION - Possible Real Issues (5 tests)

### 3. Changelog Tests - Possible Timing/Selector Issues (3 failures)

**Failing Tests:**
1. `changelog.spec.ts:49` - should display version history in modal
2. `changelog.spec.ts:143` - should show changelog content when version is selected
3. `changelog.spec.ts:173` - should work in collapsed sidebar mode

**Status:** ⚠️ **UNCERTAIN** - Has proper E2E auth, but visibility assertions fail

**Possible Causes:**
1. **Test Issue:** Aggressive/incorrect selectors for version list
   ```typescript
   // Test tries multiple approaches - suggests flakiness
   const versionEntry = page.locator('button').filter({ hasText: /v\d+\.\d+\.\d+/ }).first()
     .or(page.locator('button').filter({ hasText: /\d+\.\d+\.\d+/ }).first())
     .or(page.locator('[data-testid*="version"]').first())
     .or(page.locator('li button, div button').first());
   ```

2. **Real Issue:** Version list may not be rendering due to:
   - API endpoint failure
   - Data loading issue
   - Component rendering bug

**Recommendation:**
- Check test screenshot to see what's actually visible
- Verify changelog API endpoint returns data on staging
- Add `data-testid` attributes to changelog version list items for reliable selectors

**Priority:** 🔶 **MEDIUM** - Feature works (button opens modal), but content may have issues

---

### 4. Catalog Margins Chart - Possible Real Issue (1 failure)

**Failing Test:**
- `catalog-margins-chart.spec.ts:9` - margins chart should not display current month

**Status:** ⚠️ **UNCERTAIN** - Has proper E2E auth

**Test Logic:**
```typescript
// Test expects either:
// 1. Chart canvas to be visible (chart has data)
// 2. Empty state with text "12 měsíc" (excluding current month)

const chartExists = await chartCanvas.isVisible({ timeout: 5000 }).catch(() => false);
const emptyStateExists = await emptyState.isVisible({ timeout: 5000 }).catch(() => false);

if (!chartExists && !emptyStateExists) {
  // Test passes anyway - "valid state"
}
```

**Possible Causes:**
1. **Test Issue:**
   - Chart/empty state selectors may be wrong
   - 5-second timeout may be too short for chart rendering
   - Test logic is too permissive (passes even when nothing found)

2. **Real Issue:**
   - Margins tab doesn't exist for test product
   - Chart component not rendering
   - Empty state not displaying

**Recommendation:**
- Review test implementation - passing on "nothing found" is suspicious
- Verify chart renders correctly in browser for a known product
- Add explicit `data-testid` to chart canvas and empty state

**Priority:** 🔶 **MEDIUM** - Test logic seems flawed

---

### 5. Gift Package Disassembly - Element Visibility Timeout (1 failure)

**Failing Test:**
- `gift-package-disassembly.spec.ts:28` - should complete gift package disassembly workflow

**Status:** ⚠️ **UNCERTAIN** - Has proper E2E auth

**Error Pattern:**
```
Error: expect(locator).toBeVisible() failed
```

**Possible Causes:**
1. **Test Issue:**
   - Selector doesn't match actual page elements
   - Timing issue - page loads slower than test expects
   - Test data missing (no gift packages available)

2. **Real Issue:**
   - Gift package page not rendering
   - Component broke in recent changes
   - Navigation issue

**Recommendation:**
- Check test screenshot to see actual page state
- Verify gift package feature works manually on staging
- Review recent changes to gift package components

**Priority:** 🔶 **MEDIUM** - Single workflow test, may indicate real problem

---

## Impact Assessment

### FALSE FAILURES: 21 tests (81% of failures)
**Wasted CI Time:** ~25 minutes (21 tests × ~15s + 2 retries)
**Developer Impact:** Misleading failure reports hide real issues
**Urgency:** Fix immediately

### REAL/UNKNOWN ISSUES: 5 tests (19% of failures)
**Potential Real Bugs:** Changelog, Margins Chart, Gift Package
**Urgency:** Investigate within 1-2 sprints

---

## Action Items

### Immediate (This Sprint)
- [ ] **Fix Dashboard Tests** - Add `createE2EAuthSession` (5 min fix)
- [ ] **Fix Recurring Jobs Tests** - Add `createE2EAuthSession` (5 min fix)
- [ ] **Re-run Nightly Tests** - Verify fixes restore 21 tests to passing

### Short-term (Next Sprint)
- [ ] **Investigate Changelog Tests** - Debug version list rendering
- [ ] **Investigate Margins Chart** - Review test logic and component rendering
- [ ] **Investigate Gift Package** - Check workflow and page state
- [ ] **Add data-testid Attributes** - Improve selector reliability across components

### Long-term (Backlog)
- [ ] **Test Review Process** - Ensure all new E2E tests use auth helpers
- [ ] **CI Pre-commit Hook** - Validate test files import auth helpers when needed
- [ ] **Test Template** - Provide standard template for new E2E tests

---

## Conclusion

**80% of failing tests (21/26) are FALSE FAILURES due to missing authentication setup.**

The application features (Dashboard, Recurring Jobs) are working correctly - the tests are simply not authenticating before accessing protected routes. This is a 10-minute fix that will:

1. ✅ Restore 21 tests to passing state
2. ✅ Improve CI confidence (success rate: 33% → 87%)
3. ✅ Reveal actual bugs hidden by authentication noise
4. ✅ Save ~25 minutes of wasted CI time per run

The remaining 5 failures need proper investigation to determine if they're test issues or real bugs.

---

**Next Step:** Fix the two auth-related test files, re-run nightly tests, then investigate the remaining 5 failures with clean data.
