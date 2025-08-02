# Purchase Orders UI Tests

These Playwright tests verify the critical functionality of purchase order creation, editing, and line management.

## What These Tests Catch

### 🚨 Critical Issues That Were Missing:
1. **Lines not being sent from frontend** - The main bug we just fixed
2. **Lines not persisting in database** - EF Core backing field issues
3. **UI validation not working properly**
4. **API contract mismatches**
5. **Network error handling**

## Test Coverage

### `purchase-order-creation.spec.ts`
- ✅ **Full purchase order creation flow with lines**
- ✅ **Validation of line totals and calculations**
- ✅ **Verification that lines persist after creation**
- ✅ **Edit functionality with line modifications**
- ✅ **Error handling for missing lines**
- ✅ **Network error scenarios**

### `../api/purchase-orders-api.spec.ts`
- ✅ **Direct API testing of purchase order endpoints**
- ✅ **Verification of JSON contract compliance**
- ✅ **Database persistence validation**
- ✅ **Edge cases (missing lines, null values)**

## Why These Tests Would Have Caught The Bug

The bug we fixed had **two critical components**:

1. **Frontend Issue**: Lines were commented out with `// TODO: Add lines support to backend`
   - ❌ **Missing**: No test verified that lines are actually sent in the request
   - ✅ **Now Fixed**: `purchase-order-creation.spec.ts` tests the complete flow
   - ✅ **Now Fixed**: `purchase-orders-api.spec.ts` tests the API directly

2. **Backend Issue**: EF Core wasn't tracking backing field changes
   - ❌ **Missing**: No test verified that lines persist after creation
   - ✅ **Now Fixed**: Both tests fetch the order again to verify persistence

## Running the Tests

### Prerequisites
```bash
# Install Playwright
npm install -D @playwright/test

# Install browsers
npx playwright install
```

### Start Test Environment
```bash
# Backend (port 5001)
cd backend/src/Anela.Heblo.API
ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation &

# Frontend (port 3001) 
cd frontend
npm run start:automation &

# Wait 5 seconds for servers to start
sleep 5
```

### Run Tests
```bash
# Run all purchase order tests
npx playwright test test/ui/purchase-orders/ test/api/purchase-orders-api.spec.ts

# Run with UI (headed mode)
npx playwright test --headed test/ui/purchase-orders/

# Run specific test
npx playwright test test/ui/purchase-orders/purchase-order-creation.spec.ts

# Debug mode
npx playwright test --debug test/ui/purchase-orders/purchase-order-creation.spec.ts
```

### Cleanup
```bash
# Kill background processes
pkill -f "dotnet run"
pkill -f "npm run start:automation"
```

## Test Data-TestIDs Required

For these tests to work, the following `data-testid` attributes need to be added to the frontend components:

### Navigation
- `nav-purchase-orders` - Purchase orders navigation link

### Purchase Order List
- `new-purchase-order-button` - New purchase order button
- `purchase-order-row` - Purchase order table rows
- `view-order-button` - View order details button
- `edit-order-button` - Edit order button

### Purchase Order Form
- `purchase-order-form` - Form modal container
- `supplier-name-input` - Supplier name input
- `order-date-input` - Order date input
- `expected-delivery-date-input` - Expected delivery date input
- `notes-input` - Notes input
- `material-autocomplete-{index}` - Material autocomplete inputs
- `material-option` - Material options in dropdown
- `quantity-input-{index}` - Quantity inputs
- `unit-price-input-{index}` - Unit price inputs
- `line-notes-input-{index}` - Line notes inputs
- `line-total-{index}` - Line total displays
- `order-total` - Order total display
- `submit-purchase-order-button` - Submit button
- `lines-error` - Lines validation error
- `submit-error` - Submit error message

### Purchase Order Detail
- `purchase-order-detail` - Detail modal container
- `detail-supplier-name` - Supplier name display
- `detail-order-total` - Order total display
- `detail-notes` - Notes display
- `order-line-row` - Order line rows
- `line-material-name` - Line material name
- `line-quantity` - Line quantity
- `line-unit-price` - Line unit price
- `line-total` - Line total
- `line-notes` - Line notes

## Integration with CI/CD

Add these tests to GitHub Actions:

```yaml
- name: Run Purchase Order Tests
  run: |
    # Start backend
    cd backend/src/Anela.Heblo.API
    ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation &
    BACKEND_PID=$!
    
    # Start frontend  
    cd frontend
    npm run start:automation &
    FRONTEND_PID=$!
    
    # Wait for startup
    sleep 10
    
    # Run tests
    npx playwright test test/ui/purchase-orders/ test/api/purchase-orders-api.spec.ts --reporter=list
    
    # Cleanup
    kill $BACKEND_PID $FRONTEND_PID 2>/dev/null || true
```

## Expected Test Outcomes

✅ **Before Bug Fix**: These tests would **FAIL** with:
- Lines not appearing in API requests
- Lines count = 0 in database
- Total amount = 0 always

✅ **After Bug Fix**: These tests should **PASS** with:
- Lines properly sent in API requests
- Lines persisted in database
- Correct total calculations
- Proper error handling