# Investigation Report: TASK-019 - Issued Invoices Invoice ID Filter Assertion Failure

## Test Summary

- **Test Name**: "3: Invoice ID filter with Enter key"
- **File Location**: `frontend/test/e2e/issued-invoices/filters.spec.ts:38-65`
- **Module**: Issued Invoices
- **Test Type**: Filter functionality validation
- **Status**: ❌ FAILING

## Error Details

```
Error: Expected locator 'text="Žádné faktury nebyly nalezeny."' to be visible but element(s) not found
```

**Error location**: Test line 58 (within conditional block at lines 53-58)

## Test Scenario

The test validates Invoice ID filtering functionality using Enter key:

1. **Navigate** to Issued Invoices page (`navigateToIssuedInvoices` helper)
2. **Wait** for page to load (complex loading detection at lines 11-31)
3. **Switch** to Grid tab ("Seznam" button click at line 34)
4. **Fill** Invoice ID input field with "2024" (line 44)
5. **Press** Enter key to apply filter (line 45)
6. **Wait** for loading to complete (line 46)
7. **Count** filtered table rows (line 49)
8. **Conditional assertion** (lines 53-64):
   - **If 0 rows**: Assert empty state message "Žádné faktury nebyly nalezeny." is visible ← **TEST FAILS HERE**
   - **If >0 rows**: Assert first row contains "2024"

## Root Cause Analysis

### What is happening

The test expects one of two outcomes after filtering by "2024":
1. **No matching invoices**: Show empty state message "Žádné faktury nebyly nalezeny."
2. **Matching invoices found**: Display filtered results containing "2024"

The test is failing at line 58, attempting to assert the empty state message is visible when `filteredCount === 0`.

### Why it's happening

**Most likely cause: Empty state message component not rendering correctly**

The conditional logic indicates that:
- `filteredCount === 0` (no table rows found after filtering)
- BUT empty state message locator `text="Žádné faktury nebyly nalezeny."` is not visible

**Possible explanations:**

#### 1. **Empty State Component Missing or Different Text** (Most likely)
- The empty state message might use different wording (e.g., "Žádné záznamy", "Nenalezeny žádné faktury")
- The message might be in a different DOM structure (e.g., inside a div with specific class, not plain text)
- The component might not render at all for empty filtered results

#### 2. **API Returns Empty Array But No UI Feedback**
- Backend successfully returns empty array `[]` for "2024" filter
- Frontend receives empty response correctly
- BUT empty state component doesn't render or has rendering bug
- Table shows 0 rows but no empty state message appears

#### 3. **Loading State Never Completes**
- `waitForLoadingComplete` might return before actual loading finishes
- Table might still be in loading/empty state (skeleton, spinner)
- Empty state message might be hidden behind loading overlay
- Race condition between loading complete and message rendering

#### 4. **Test Data Issue**
- Staging environment might have invoices matching "2024"
- Test assumes filtering will return empty results
- But staging actually has matching data → `filteredCount > 0`
- Test never enters the failing conditional block under normal circumstances
- This suggests recent data drift or data cleanup on staging

### Affected Code Locations

**Test file**: `frontend/test/e2e/issued-invoices/filters.spec.ts:38-65`

**Key lines:**
- Line 44: `await invoiceIdInput.fill("2024");` - Filter value
- Line 45: `await invoiceIdInput.press("Enter");` - Trigger filter
- Line 46: `await waitForLoadingComplete(page);` - Wait for results
- Line 49: `const filteredCount = await tableRows.count();` - Count results
- Line 53-58: Conditional assertion that fails (empty state message not visible)

**Component** (not yet identified):
- Issued Invoices page component (likely `IssuedInvoicesList.tsx` or similar)
- Empty state component for filtered results
- Table component implementation

**Wait helper**: `frontend/test/e2e/helpers/wait-helpers.ts:waitForLoadingComplete`

## Pattern Differences from Previous Investigations

**This is NOT the catalog timeout pattern** (TASK-001 through TASK-018):
- ✅ No redundant `waitForTableUpdate` call
- ✅ Test uses proper waiting pattern (`waitForLoadingComplete`)
- ✅ No React Query cache issue
- ✅ No API response timeout

**This is a genuine assertion failure:**
- Test expects empty state message when no results found
- Message component either doesn't exist or uses different selector
- Indicates potential UI/UX bug or test selector issue

## Impact Assessment

**User-Facing Functionality Affected:**

If the empty state message truly doesn't render:
- **Severity**: Medium - Poor UX but not blocking
- **User Impact**: When filtering returns no results, users see blank table without feedback
- **Workaround**: Users can infer no results from empty table, but explicit message is better UX

If test selector is wrong but message renders:
- **Severity**: Low - Test reliability issue only
- **User Impact**: None - feature works correctly
- **Resolution**: Update test selector to match actual message

## Empirical Validation Needed

Cannot determine definitive root cause from code analysis alone. Need to:

1. **Run test in debug mode**:
   ```bash
   npx playwright test issued-invoices/filters.spec.ts:38 --headed --debug
   ```

2. **Inspect staging environment**:
   - Navigate to https://heblo.stg.anela.cz/issued-invoices
   - Apply filter "2024" in Invoice ID field
   - Check if results are empty or contain data
   - If empty, observe what message (if any) is displayed

3. **Check component implementation**:
   - Find IssuedInvoices page component
   - Locate empty state message rendering logic
   - Verify actual message text and DOM structure

4. **Verify test data**:
   - Check staging database for invoices containing "2024" in ID
   - Determine if test expectation (empty results) matches reality

## Fix Proposal

### Scenario A: Message Text is Different

**If message renders but with different text:**

```typescript
// Update selector to match actual message
const emptyMessage = page.locator('text="Nenalezeny žádné faktury."'); // or actual text
await expect(emptyMessage).toBeVisible();
```

**Estimated complexity**: Simple (5 minutes)

### Scenario B: Message in Different DOM Structure

**If message renders in specific element:**

```typescript
// Use more specific selector
const emptyMessage = page.locator('.empty-state-message'); // or actual selector
await expect(emptyMessage).toBeVisible();
await expect(emptyMessage).toContainText('faktury'); // partial text match
```

**Estimated complexity**: Simple (10 minutes)

### Scenario C: Component Not Rendering

**If empty state component genuinely doesn't render:**

1. Locate IssuedInvoices page component
2. Add/fix empty state rendering logic:
   ```typescript
   {filteredData.length === 0 && <EmptyStateMessage message="Žádné faktury nebyly nalezeny." />}
   ```
3. Update test to match implementation

**Estimated complexity**: Medium (30-60 minutes) - Requires component modification

### Scenario D: Test Data Drift

**If staging now has invoices matching "2024":**

```typescript
// Use intentionally nonexistent filter value
await invoiceIdInput.fill("NONEXISTENT_INVOICE_99999");
await invoiceIdInput.press("Enter");
await waitForLoadingComplete(page);

const filteredCount = await tableRows.count();
if (filteredCount === 0) {
  const emptyMessage = page.locator('text="Žádné faktury nebyly nalezeny."');
  await expect(emptyMessage).toBeVisible();
}
```

**Estimated complexity**: Simple (5 minutes) - Change test data

### Scenario E: Race Condition in Loading

**If message appears after delay:**

```typescript
// Add explicit wait for message or table stabilization
await page.waitForLoadState('networkidle');
await page.waitForTimeout(500); // Small stabilization wait

if (filteredCount === 0) {
  const emptyMessage = page.locator('text="Žádné faktury nebyly nalezeny."');
  await expect(emptyMessage).toBeVisible({ timeout: 10000 }); // Longer timeout
}
```

**Estimated complexity**: Simple (10 minutes)

## Recommended Approach

1. **Run test in debug mode** to observe actual page state
2. **Inspect staging environment** to see what message (if any) renders
3. **Based on observations, apply appropriate fix** from scenarios above
4. **Verify fix** by running test multiple times to ensure stability

## Related Failures

**None directly related** in this investigation brief. This is the only issued-invoices filter test failure.

However, note that many tests in `filters.spec.ts` are **skipped** (tests #4-9) with comments indicating:
- "Systematic application bug affecting ALL 43 issued-invoices tests"
- "Issued Invoices page doesn't render tabs properly"
- "Backend investigation needed: Verify /api/issued-invoices endpoint"

These skipped tests suggest there MAY be broader issued-invoices module issues. However, test #3 (this failing test) and tests #10-11 are NOT skipped, indicating they at least navigate successfully, so the systematic issue may be partially resolved or only affect certain test scenarios.

## Investigation Conclusion

**Root cause: Cannot determine definitively without empirical validation**

**Most likely**: Empty state message component either:
- Doesn't render for filtered empty results (UI bug)
- Uses different text or DOM structure (test selector issue)

**Next steps**:
1. Run test in debug mode
2. Inspect staging environment
3. Apply appropriate fix based on observations

**Confidence level**: Medium - Need empirical data to confirm exact issue

**Estimated fix time**: 5-60 minutes depending on root cause scenario
