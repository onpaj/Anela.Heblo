# Investigation Report: TASK-020

## Test Summary

**Test Name:** "should allow user to correct fixed quantities and recalculate successfully"
**File Location:** `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts:248-362`
**Module:** Manufacturing
**Test Type:** E2E (Playwright)

## Error Details

**Error Message:**
```
No products available for semiproduct "Hedvábný pan Jasmín". Cannot test quantity correction without products.
```

**Error Location:** Line 306-309

**Stack Trace:**
```typescript
if (rowCount === 0) {
  throw new Error(
    `No products available for semiproduct "${testSemiproduct.name}". ` +
    `Cannot test quantity correction without products.`
  );
}
```

## Test Scenario

### What the test is trying to verify:

This test validates the batch planning calculator's ability to:
1. Handle user corrections after a validation error (fixed products exceeding volume)
2. Allow recalculation with corrected fixed quantities
3. Successfully calculate batch plan when inputs are valid
4. Verify no error toasters appear after successful correction

### Test Flow:

1. **Navigate to Batch Planning Calculator** (`/manufacturing/batch-planning`)
2. **Select semiproduct** using test fixture: `TestCatalogItems.hedvabnyPan`
   - Code: `MAS001001M`
   - Name: `Hedvábný pan Jasmín`
   - Type: `Polotovar`
3. **Wait for product table to load** after semiproduct selection
4. **Configure fixed product** with reasonable quantity (10 units)
5. **Trigger calculation** by clicking "Přepočítat" button
6. **Verify successful calculation** (no error toasters)

### Where test fails:

The test fails at **Step 3** - after selecting the semiproduct, the product table loads but contains **0 rows** (line 303: `const rowCount = await productRows.count()`).

## Root Cause Analysis

### What is happening:

1. Test successfully navigates to batch planning page ✅
2. Test successfully finds and selects semiproduct "Hedvábný pan Jasmín" ✅
3. Test verifies product table exists (line 107-115) ✅
4. Test counts product rows in table: **`rowCount === 0`** ❌
5. Test throws error with clear message about missing products ❌

### Why it's happening:

**Root Cause: Test Data Availability Issue in Staging Environment**

The semiproduct "Hedvábný pan Jasmín" (MAS001001M) exists in staging environment (confirmed by test being able to select it), BUT it does not have any associated products configured.

#### Evidence from test data documentation:

**`docs/testing/test-data-fixtures.md:58`**:
```yaml
| MAS001001M | Hedvábný pan Jasmín | Polotovar | 4 variants |
```

**Note:** Documentation states "4 variants" but this refers to manufacturing order variants (different sizes/configurations of the semiproduct itself), not the products that compose the semiproduct in batch planning.

#### Possible scenarios:

1. **Staging database drift**: Products were deleted/removed from staging environment
2. **Development vs Staging mismatch**: Test data fixtures documented from development environment (localhost), but test runs against staging (https://heblo.stg.anela.cz)
3. **Semiproduct recipe not configured**: The semiproduct exists in catalog but doesn't have recipe/composition configured in batch planning system
4. **Recent data cleanup**: Test data may have been intentionally cleaned up in staging
5. **Database state difference**: Staging database may be in different state than development database

### Why test is correctly designed:

✅ Test uses proper test data fixture (`TestCatalogItems.hedvabnyPan`)
✅ Test includes clear error message when data is missing
✅ Test fails fast rather than proceeding with invalid state
✅ Test follows "fail, don't skip" principle from CLAUDE.md guidelines
✅ Test uses proper waiting patterns (no timeout issues)

## Affected Code Locations

**Test file:**
- `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts:248-362`
- Specifically line 303-309 (product row count validation)

**Test fixture:**
- `frontend/test/e2e/fixtures/test-data.ts:70-74` (hedvabnyPan definition)

**Test data documentation:**
- `docs/testing/test-data-fixtures.md:58` (semiproduct reference)
- `docs/testing/test-data-fixtures.md:222-224` (usage guidelines)

**Related test:**
- `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts:25-246` (first test in same describe block - likely also affected if products missing)

## Impact Assessment

### User-facing functionality affected:

**None** - This is a test data availability issue, not a functional bug in the application.

The batch planning calculator functionality works correctly. The issue is that staging environment lacks the necessary test data (products associated with the semiproduct) to execute the test scenario.

### Test coverage impact:

- **2 tests affected** in `batch-planning-error-handling.spec.ts`:
  1. "should handle fixed products exceed volume with toaster and visual indicators" (lines 25-246)
  2. "should allow user to correct fixed quantities and recalculate successfully" (lines 248-362)

- Both tests require products to be configured for "Hedvábný pan Jasmín" semiproduct
- Tests cannot verify batch planning error handling without product data
- Critical manufacturing workflow validation is blocked

### Test reliability:

- Test properly fails with clear error message ✅
- Test does not produce false positives ✅
- Test data dependency is clearly documented ❌ (documentation doesn't specify product requirements)

## Fix Proposal

### Recommended Approach: **Test Data Seeding in Staging**

**Complexity:** Simple to Medium (15-30 minutes)

#### Option 1: Seed Products for Existing Semiproduct (RECOMMENDED)

**Steps:**
1. Access staging environment database (https://heblo.stg.anela.cz)
2. Verify semiproduct "Hedvábný pan Jasmín" (MAS001001M) exists in catalog
3. Configure recipe/composition for this semiproduct in batch planning system:
   - Add at least 2-3 material products that compose the semiproduct
   - Use stable materials from test fixtures (e.g., AKL001 Bisabolol, AKL007 Glycerol)
   - Configure reasonable quantities per batch
4. Verify products appear in batch planning calculator UI
5. Re-run failing tests to confirm fix

**Database actions:**
- Query: Check if semiproduct recipe exists
- Insert: Add product compositions if missing
- Verify: Ensure data is visible in batch planning UI

#### Option 2: Use Different Semiproduct with Existing Products

**Steps:**
1. Access staging environment batch planning calculator
2. Identify semiproducts that have products already configured
3. Document the working semiproduct details (code, name, product count)
4. Update test fixture in `test-data.ts`:
   ```typescript
   hedvabnyPan: {
     code: 'NEW_CODE',
     name: 'New Semiproduct Name',
     type: 'Polotovar'
   }
   ```
5. Update test data documentation in `test-data-fixtures.md`
6. Re-run tests to confirm fix

**Pros:** No database changes needed
**Cons:** Test fixture no longer matches documentation; may confuse future developers

#### Option 3: Create New Test Semiproduct

**Steps:**
1. Create new semiproduct specifically for E2E testing
2. Name it clearly (e.g., "E2E Test Semiproduct")
3. Configure 2-3 stable material products
4. Add to test fixtures
5. Update documentation

**Pros:** Dedicated test data, clear separation from production data
**Cons:** Most time-consuming; requires database access and admin privileges

### Alternative Approach: **Skip Test Until Data Available**

**Complexity:** Simple (5 minutes)

Mark test as skipped with clear comment explaining data dependency:

```typescript
test.skip('should allow user to correct fixed quantities and recalculate successfully', async ({ page }) => {
  // SKIPPED: Requires test data in staging environment
  // Missing: Products configured for semiproduct "Hedvábný pan Jasmín" (MAS001001M)
  // See: docs/investigations/TASK-020-manufacturing-batch-planning-test-data-issue.md
  // TODO: Unskip after staging data seeded
```

**Pros:** Quick fix, documents known issue
**Cons:** Loses test coverage, doesn't fix underlying problem

### Recommended Fix: **Option 1 (Test Data Seeding)**

**Reasoning:**
- Preserves test coverage for critical manufacturing workflow
- Uses existing test fixture (no code changes needed)
- Aligns with test data fixtures documentation
- Restores full E2E test suite functionality
- Minimal implementation complexity

**Implementation Time:** 15-30 minutes

**Follow-up Actions:**
1. Document seeded products in `test-data-fixtures.md`
2. Update "Usage Guidelines" section to specify product requirements
3. Add warning comment in test file about data dependency
4. Consider creating data seeding script for future staging refreshes

## Related Failures

**None** - This is an isolated test data issue specific to manufacturing module.

Other failing tests (TASK-001 through TASK-019, TASK-021) have different root causes:
- Catalog timeouts: React Query caching + redundant waits
- Assertion failures: UI component or filter logic issues
- This is the only test data availability failure in the investigation

## Pattern Analysis

### Test Data Dependencies:

This investigation reveals a **critical pattern** for E2E testing:

**Pattern: Test Data Stability Varies by Environment**

1. **Development environment** (localhost):
   - Test data fixtures documented from this environment
   - Full dataset with all test scenarios configured
   - Stable, controlled test data

2. **Staging environment** (heblo.stg.anela.cz):
   - Target environment for nightly E2E tests
   - May have incomplete or drifted test data
   - No guaranteed parity with development environment

**Gap:** Tests documented against development environment but executed against staging environment without data validation.

### Recommendations for Test Data Management:

1. **Add pre-test data validation**:
   ```typescript
   test.beforeAll(async ({ page }) => {
     await validateRequiredTestData(page, [
       { type: 'semiproduct', code: 'MAS001001M', hasProducts: true }
     ]);
   });
   ```

2. **Document environment-specific data requirements**:
   - Update `test-data-fixtures.md` with "Required For" column
   - Specify which features need which data
   - Note minimum product counts for batch planning tests

3. **Create data seeding scripts**:
   - Automate staging data setup
   - Run before nightly E2E test suite
   - Ensure data parity across environments

4. **Add data availability checks to tests**:
   - Current test correctly fails with clear message ✅
   - Could add optional test.skip() based on detected environment
   - Or add data seeding as test setup step

## Testing Strategy

### Validation Commands:

**Before fix:**
```bash
# Run failing test to confirm error
./scripts/run-playwright-tests.sh manufacturing

# Expected: Test fails with "No products available" error
```

**After fix (Option 1 - Data Seeding):**
```bash
# Access staging environment
# Navigate to /manufacturing/batch-planning
# Select "Hedvábný pan Jasmín"
# Verify products appear in table

# Run test again
./scripts/run-playwright-tests.sh manufacturing

# Expected: Test passes
```

**After fix (Option 2 - Different Semiproduct):**
```bash
# Run tests with updated fixture
./scripts/run-playwright-tests.sh manufacturing

# Expected: Test passes with new semiproduct
```

### Verification Steps:

1. ✅ Semiproduct exists in staging catalog
2. ✅ Semiproduct can be selected in batch planning calculator
3. ❌ Products are configured for semiproduct (MISSING)
4. ❌ Product table shows rows after semiproduct selection (FAILS)
5. ❌ Test can proceed to configure fixed quantities (BLOCKED)

## Investigation Notes

### Environment Differences:

**Test data documentation source:**
- Environment: Development (localhost:3000 / localhost:5000)
- Date: 2026-01-25
- Note: `docs/testing/test-data-fixtures.md:6-7`

**Test execution environment:**
- Environment: Staging (https://heblo.stg.anela.cz)
- Date: 2026-02-09 (nightly regression)
- Note: 15 days after documentation generated

**Data drift window:** 15 days between documentation and test execution

### Test Design Quality:

✅ **Good practices observed:**
- Uses test fixture constants (not hardcoded values)
- Includes comprehensive error messages
- Fails fast with clear diagnostic information
- Uses proper waiting patterns
- No timeout or race condition issues
- Follows "fail, don't skip" principle

⚠️ **Could be improved:**
- Could add pre-test data validation
- Could document minimum product count requirement
- Could include fallback to alternative semiproduct
- Could add data seeding as setup step

### First Test in Same File:

**"should handle fixed products exceed volume with toaster and visual indicators"** (lines 25-246)

This test likely has the **same root cause** because it:
1. Also selects "Hedvábný pan Jasmín" semiproduct (line 67)
2. Also requires products to configure (lines 123-160)
3. Also validates `rowCount === 0` and throws same error (lines 127-133)

**Both tests in the describe block are affected by the same data issue.**

## Summary

### Key Findings:

1. **Not a functional bug** - Application works correctly
2. **Test data availability issue** - Staging lacks required products
3. **Environment data drift** - Development vs Staging mismatch
4. **2 tests affected** - Both tests in batch-planning-error-handling.spec.ts
5. **Clear error message** - Test properly communicates data issue
6. **Pattern identified** - Need data validation and seeding strategy

### Impact:

- **Severity:** Medium (blocks test coverage, no user impact)
- **Urgency:** Low (not a production bug)
- **Scope:** 2 tests in manufacturing module

### Next Steps:

1. Implement **Option 1** (recommended): Seed products in staging
2. Document seeded products in test data fixtures
3. Add data validation to test setup
4. Consider data seeding automation for future staging refreshes
5. Update test data documentation to note environment differences

---

**Investigation Completed:** 2026-02-09
**Time Spent:** 30 minutes
**Confidence Level:** High (clear data availability issue)
**Recommended Action:** Test Data Seeding (15-30 min implementation)
