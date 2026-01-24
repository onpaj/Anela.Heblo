# E2E Test Data Requirements

## Current Problem

Many E2E tests are **skipping silently** on staging because required test data doesn't exist. This results in:
- ❌ False sense of security (tests "pass" but don't run)
- ❌ No real validation of features
- ❌ Bugs potentially slipping through

## Tests Requiring Specific Data

### 1. Catalog/Product Data
**Required by:**
- `date-handling.spec.ts` - Needs catalog items with date fields
- `catalog-ui.spec.ts` - Needs product list to validate UI
- `catalog-margins-chart.spec.ts` - Needs products with margin data
- `catalog-product-type-filter.spec.ts` - Needs various product types

**Minimum Requirements:**
- At least 5 products in catalog
- Products should have:
  - Valid EAN codes
  - Price data
  - Margin information
  - Different product types (material, product, semi-product)
  - Expiration dates (for date handling tests)

### 2. Manufacturing Data
**Required by:**
- `manufacture-batch-planning-workflow.spec.ts` - Needs specific product code `MAS001001M`
- `manufacture-order-creation.spec.ts` - Needs semi-products for order creation
- `batch-planning-error-handling.spec.ts` - Needs semi-products to test error scenarios

**Minimum Requirements:**
- At least 3 semi-products including `MAS001001M`
- Products should have:
  - Bill of materials (BOM) data
  - MMQ (Minimum Manufacturable Quantity) values
  - Component relationships

### 3. Transport/Inventory Data
**Required by:**
- `transport-box-*.spec.ts` tests - Need transport boxes and EAN codes
- All inventory-related tests

**Minimum Requirements:**
- At least 5 transport boxes in various states (Created, Packed, Sent, Received, Stocked)
- EAN codes for products

### 4. Invoice Data
**Required by:**
- `invoice-classification-history.spec.ts` - Needs invoice records

**Minimum Requirements:**
- At least 3 invoice records with classification history

## Proposed Solutions

### Option 1: Dedicated Test Data Seeding (RECOMMENDED)

**Approach:** Create a database seeding script that populates staging with consistent test data

**Pros:**
- ✅ Tests run reliably every time
- ✅ Consistent test environment
- ✅ Easy to reset/refresh test data
- ✅ Can run as part of deployment pipeline

**Cons:**
- ⚠️ Requires maintaining seed scripts
- ⚠️ Seed data might conflict with real staging usage

**Implementation:**
```bash
# Add to deployment pipeline or run manually
dotnet run --project backend/scripts/SeedTestData.csproj -- --environment Staging
```

### Option 2: Test Data Creation in Tests

**Approach:** Each test creates its own required data via API calls in `beforeEach()`

**Pros:**
- ✅ Tests are self-contained
- ✅ No dependency on external data
- ✅ Easier to understand test requirements

**Cons:**
- ⚠️ Slower test execution (creates data every time)
- ⚠️ More complex test setup
- ⚠️ Potential data pollution in database

**Example:**
```typescript
test.beforeEach(async ({ page, request }) => {
  // Create test product via API
  const product = await createTestProduct(request, {
    code: 'TEST-PRODUCT-001',
    name: 'Test Product',
    price: 100
  });

  // Store ID for cleanup
  testData.push(product.id);
});

test.afterEach(async ({ request }) => {
  // Cleanup created data
  await deleteTestData(request, testData);
});
```

### Option 3: Environment-Specific Test Suites

**Approach:** Different test suites for different environments

**Pros:**
- ✅ Can run basic tests always
- ✅ Full tests only where data exists

**Cons:**
- ⚠️ Maintains multiple test configurations
- ⚠️ Harder to ensure complete coverage

**Configuration:**
```typescript
// playwright.config.ts
projects: [
  {
    name: 'staging-basic',
    testMatch: /.*\.basic\.spec\.ts/,
    use: { baseURL: 'https://heblo.stg.anela.cz' }
  },
  {
    name: 'staging-full',
    testMatch: /.*\.spec\.ts/,
    use: { baseURL: 'https://heblo.stg.anela.cz' },
    grep: /@requires-data/  // Only run tests tagged with data requirements
  }
]
```

## Recommended Approach

**Hybrid Solution:**

1. **Create minimal seed data script** for staging environment
   - Seeds ~10 products, 5 transport boxes, basic manufacturing data
   - Runs after each staging deployment
   - Idempotent (can run multiple times safely)

2. **Make tests more resilient** with better data detection
   - Instead of `test.skip()`, use `test.fail()` with clear error messages
   - Log what data is missing to help with debugging
   - Use `test.fixme()` for tests that genuinely need work

3. **Tag data-dependent tests** with annotations
   ```typescript
   test('should display product list', async ({ page }) => {
     test.slow(); // Mark as slower test
     // ... test code
   });
   ```

4. **Create cleanup mechanism** for test data
   - Tag test data with `isTestData: true` flag
   - Periodic cleanup job removes old test data
   - Prevents staging environment pollution

## Implementation Priority

### Phase 1: Immediate (This Week)
- [ ] Create minimal seed script for top 5 most-skipped tests
- [ ] Document required test data in this file
- [ ] Replace `test.skip()` with `test.fail()` + error messages

### Phase 2: Short-term (Next Sprint)
- [ ] Implement full seeding strategy
- [ ] Add seeding to CI/CD deployment pipeline
- [ ] Create test data cleanup mechanism

### Phase 3: Long-term (Future)
- [ ] Explore test data creation within tests (Option 2)
- [ ] Create test data management API endpoints
- [ ] Build test data reset/refresh UI in admin panel

## Current Status

**Tests Skipping:**
- `date-handling.spec.ts` - All 3 tests skip (no catalog items)
- `batch-planning-error-handling.spec.ts` - Skips (no semi-products)
- Various conditional skips across other tests

**Impact:**
- ~10-15 tests not running on staging
- ~30% of E2E test suite potentially not validating

**Next Steps:**
1. Create initial seed script with minimal data
2. Run seed script on staging
3. Verify tests pass with seeded data
4. Integrate into deployment pipeline
