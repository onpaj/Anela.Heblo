# E2E Test Module Guide

This document defines the modular structure of E2E tests, module boundaries, and guidelines for adding new tests.

## Module Overview

The E2E test suite is organized into 6 logical modules to enable parallel execution in CI/CD:

| Module | Test Count | Purpose | Estimated Runtime |
|--------|-----------|---------|-------------------|
| **catalog** | 9 | Catalog page functionality | 2-3 min |
| **issued-invoices** | 6 | Issued invoices management | 2 min |
| **stock-operations** | 9 | Stock operations workflow | 2-3 min |
| **transport** | 7 | Transport box management | 2-3 min |
| **manufacturing** | 4 | Manufacturing orders & batches | 1-2 min |
| **core** | 10 | Core features & navigation | 2 min |

## Module Definitions

### 1. Catalog Module (`catalog/`)

**Purpose:** Tests for the Catalog page, including product filtering, sorting, pagination, and margin charts.

**Scope:**
- Product search and filtering (by type, text, etc.)
- Sorting functionality
- Pagination with filters
- Margin charts and visualizations
- UI components on catalog page

**Test Files:**
- `clear-filters.spec.ts` - Filter clearing functionality
- `combined-filters.spec.ts` - Multiple filters applied together
- `filter-edge-cases.spec.ts` - Edge cases in filtering
- `margins-chart.spec.ts` - Margin chart visualization
- `pagination-with-filters.spec.ts` - Pagination with active filters
- `product-type-filter.spec.ts` - Product type filtering
- `sorting-with-filters.spec.ts` - Sorting combined with filters
- `text-search-filters.spec.ts` - Text-based product search
- `ui.spec.ts` - General catalog UI components

### 2. Issued Invoices Module (`issued-invoices/`)

**Purpose:** Tests for Issued Invoices page, including filters, import functionality, navigation, and status management.

**Scope:**
- Invoice filtering and search
- Invoice import modal
- Navigation between invoice views
- Pagination
- Sorting invoices
- Status badge display

**Test Files:**
- `filters.spec.ts` - Invoice filtering functionality
- `import-modal.spec.ts` - Invoice import workflow
- `navigation.spec.ts` - Navigation within issued invoices
- `pagination.spec.ts` - Invoice list pagination
- `sorting.spec.ts` - Invoice sorting
- `status-badges.spec.ts` - Invoice status badge display

### 3. Stock Operations Module (`stock-operations/`)

**Purpose:** Tests for Stock Operations page, including operation management, filtering, retry functionality, and status tracking.

**Scope:**
- Stock operation filtering
- Operation retry workflow
- Status badges and state management
- Source filtering
- State filtering
- Accept failed operations
- Operation panel details

**Test Files:**
- `accept.spec.ts` - Accepting failed stock operations
- `badges.spec.ts` - Status badge display
- `filters.spec.ts` - General operation filtering
- `navigation.spec.ts` - Navigation within stock operations
- `panel.spec.ts` - Operation details panel
- `retry.spec.ts` - Retry failed operations
- `sorting.spec.ts` - Operation sorting
- `source-filter.spec.ts` - Filtering by operation source
- `state-filter.spec.ts` - Filtering by operation state

### 4. Transport Module (`transport/`)

**Purpose:** Tests for Transport Box management, including box creation, item management, receiving workflow, and EAN integration.

**Scope:**
- Transport box creation
- Box item management
- Box receiving workflow
- EAN code scanning and integration
- Box management operations
- Complete box workflow

**Test Files:**
- `box-creation.spec.ts` - Creating new transport boxes
- `box-items.spec.ts` - Managing items within boxes
- `box-management.spec.ts` - Box management operations
- `box-receive.spec.ts` - Receiving transport boxes
- `box-workflow.spec.ts` - Complete box workflow
- `boxes-basic.spec.ts` - Basic box functionality
- `ean-integration.spec.ts` - EAN code integration

### 5. Manufacturing Module (`manufacturing/`)

**Purpose:** Tests for Manufacturing functionality, including batch planning, order creation, and order state management.

**Scope:**
- Batch planning workflow
- Batch planning error handling
- Manufacturing order creation
- Order state transitions

**Test Files:**
- `batch-planning-error-handling.spec.ts` - Error handling in batch planning
- `batch-planning-workflow.spec.ts` - Complete batch planning workflow
- `order-creation.spec.ts` - Creating manufacturing orders
- `order-state-return.spec.ts` - Order state transitions

### 6. Core Module (`core/`)

**Purpose:** Tests for core application features, navigation, authentication, and shared functionality.

**Scope:**
- Dashboard functionality
- Changelog page
- Sidebar navigation
- Authentication flows
- Invoice classification history
- Recurring jobs management
- Gift package disassembly
- Debug pages

**Test Files:**
- `changelog.spec.ts` - Changelog page functionality
- `dashboard.spec.ts` - Dashboard functionality
- `debug-transport-page.spec.ts` - Debug transport page
- `gift-package-disassembly.spec.ts` - Gift package disassembly workflow
- `invoice-classification-history.spec.ts` - Invoice classification history
- `invoice-classification-history-actions.spec.ts` - Classification history actions
- `invoice-classification-history-filters.spec.ts` - Classification history filters
- `recurring-jobs-management.spec.ts` - Recurring jobs management
- `sidebar-navigation.spec.ts` - Sidebar navigation
- `staging-auth.spec.ts` - Staging authentication

## Module Boundaries & Isolation

### Data Isolation

**Critical:** Modules should NOT share mutable test data to ensure parallel execution safety.

**Guidelines:**
- Use distinct test data for each module when possible
- Reference `fixtures/test-data.ts` for shared read-only data
- Avoid creating tests that modify shared state
- If shared state modification is necessary, document dependencies

### Test Independence

**Each module should:**
- ✅ Execute independently of other modules
- ✅ Not depend on test execution order
- ✅ Clean up after itself (if modifying data)
- ✅ Use proper wait helpers for async operations

**Each module should NOT:**
- ❌ Depend on tests from other modules
- ❌ Share session state across modules
- ❌ Assume specific test execution sequence

## Adding New Tests

### 1. Determine Correct Module

Ask yourself:
- What feature does this test cover?
- Which page/workflow does it primarily test?
- Is there a logical grouping with existing tests?

### 2. Place Test in Appropriate Module

```bash
# Example: Adding a new catalog filter test
frontend/test/e2e/catalog/new-filter.spec.ts

# Example: Adding a new stock operation test
frontend/test/e2e/stock-operations/new-operation.spec.ts
```

### 3. Use Correct Import Paths

All tests in modules must use `../` for helpers and fixtures:

```typescript
// ✅ CORRECT
import { navigateToCatalog } from '../helpers/e2e-auth-helper';
import { TestCatalogItems } from '../fixtures/test-data';

// ❌ WRONG
import { navigateToCatalog } from './helpers/e2e-auth-helper';
```

### 4. Follow Module Conventions

- Use descriptive test file names (e.g., `filter-by-date.spec.ts`)
- Keep tests focused on single feature/aspect
- Use shared test helpers from `helpers/`
- Reference test data from `fixtures/test-data.ts`

### 5. Verify Module Assignment

Run the specific module to ensure tests execute correctly:

```bash
./scripts/run-playwright-tests.sh catalog
./scripts/run-playwright-tests.sh issued-invoices
# etc.
```

## Running Tests by Module

### Local Development

```bash
# Run all modules
./scripts/run-playwright-tests.sh

# Run specific module
./scripts/run-playwright-tests.sh catalog
./scripts/run-playwright-tests.sh issued-invoices
./scripts/run-playwright-tests.sh stock-operations
./scripts/run-playwright-tests.sh transport
./scripts/run-playwright-tests.sh manufacturing
./scripts/run-playwright-tests.sh core

# Run by pattern (searches across all modules)
./scripts/run-playwright-tests.sh auth
./scripts/run-playwright-tests.sh filter
```

### CI/CD Execution

In GitHub Actions, all modules run in parallel automatically via the nightly regression workflow:

```yaml
strategy:
  fail-fast: false
  matrix:
    module:
      - catalog
      - issued-invoices
      - stock-operations
      - transport
      - manufacturing
      - core
```

## Performance Characteristics

### Expected Runtimes

| Module | Test Count | Estimated Runtime |
|--------|-----------|-------------------|
| catalog | 9 | 2-3 min |
| issued-invoices | 6 | 2 min |
| stock-operations | 9 | 2-3 min |
| transport | 7 | 2-3 min |
| manufacturing | 4 | 1-2 min |
| core | 10 | 2 min |

**Total Sequential:** 10-15 minutes
**Total Parallel (CI/CD):** 3-5 minutes (limited by slowest module)

### Speedup Calculation

- **Sequential execution:** Sum of all module runtimes
- **Parallel execution:** Max runtime of slowest module
- **Expected speedup:** 3-4x improvement

## Troubleshooting Parallel Execution

### Common Issues

**1. Test Data Conflicts**
- **Symptom:** Tests pass individually but fail in parallel
- **Solution:** Check for shared mutable test data, ensure data isolation

**2. Timing Issues**
- **Symptom:** Intermittent failures in CI but passes locally
- **Solution:** Review wait helpers, ensure proper async handling

**3. Resource Contention**
- **Symptom:** Slowdowns when running all modules
- **Solution:** Check staging environment load, consider test data isolation

**4. Import Path Errors**
- **Symptom:** Module fails with "cannot find module" errors
- **Solution:** Verify import paths use `../` prefix for helpers/fixtures

## Best Practices

### Do's ✅

- Place tests in appropriate module based on feature
- Use consistent naming conventions within modules
- Leverage shared helpers from `helpers/` directory
- Reference test data from `fixtures/test-data.ts`
- Ensure tests can run independently
- Document cross-module dependencies (if any)

### Don'ts ❌

- Don't create tests that depend on other modules
- Don't share mutable state across modules
- Don't assume test execution order
- Don't create new modules without discussion
- Don't use absolute paths in import statements
- Don't skip using test data fixtures

## Future Considerations

### Module Expansion

If a module grows too large (>15 tests or >5 min runtime):
1. Consider splitting into sub-modules
2. Identify logical groupings within the module
3. Update Playwright config with new projects
4. Update CI/CD workflow matrix

### New Module Creation

Before creating a new module:
1. Ensure at least 5+ tests justify the module
2. Define clear module boundaries
3. Update this documentation
4. Update `playwright.config.ts`
5. Update GitHub Actions workflow
6. Update `CLAUDE.md`

## References

- **Playwright Config:** `frontend/playwright.config.ts`
- **GitHub Actions Workflow:** `.github/workflows/e2e-nightly-regression.yml`
- **Test Script:** `scripts/run-playwright-tests.sh`
- **Test Data Fixtures:** `docs/testing/test-data-fixtures.md`
- **Playwright Testing Guide:** `docs/testing/playwright-e2e-testing.md`
- **Project Guidelines:** `CLAUDE.md`
