# Manual Testing Guide: US-017 - Historical Orders Verification

**User Story**: US-017 - Manual testing: Verify historical orders still readable

**Test Date**: 2026-02-03

**Tester**: Manual QA

**Environment**: Staging (https://heblo-test-staging.azurewebsites.net)

---

## Overview

This document provides a step-by-step manual testing procedure to verify that historical manufacturing orders created with the old flow (before single-phase manufacturing was introduced) remain accessible and readable after the refactoring.

**Key Changes in Migration `20251020081944_AddSinglePhaseManufacturing`:**
- Added `ManufactureType` field (defaults to `MultiPhase` = 0 for existing orders)
- Replaced `SemiProductPlannedDate` and `ProductPlannedDate` with unified `PlannedDate`
- Data migration copied `SemiProductPlannedDate` → `PlannedDate` for existing orders

**Expected Behavior**:
- Historical orders should display with `ManufactureType = MultiPhase`
- All historical order data should be readable without errors
- Calendar views should show historical orders correctly
- Reports should include historical orders

---

## Prerequisites

- Access to staging environment: https://heblo-test-staging.azurewebsites.net
- Microsoft Entra ID credentials for authentication
- Knowledge of manufacturing orders created before 2025-10-20 (migration date)
- Access to browser developer tools for API inspection (optional)

---

## Test Scenarios

### Scenario 1: Query Historical Manufacturing Orders via API

#### Test Objective
Verify that historical manufacturing orders can be queried successfully via the API without errors.

#### Test Steps

1. **Navigate to Manufacturing Module**
   - Login to staging environment: https://heblo-test-staging.azurewebsites.net
   - Navigate to the Manufacturing page

2. **Identify Historical Orders**
   - Look for manufacturing orders with creation dates **before 2025-10-20**
   - Record at least 2-3 historical order numbers:
     - Order 1: `_______________` Created: `_______________`
     - Order 2: `_______________` Created: `_______________`
     - Order 3: `_______________` Created: `_______________`

3. **Verify Orders List Loads Without Errors**
   - Open browser developer console (F12)
   - Navigate to Network tab
   - Reload the manufacturing orders list
   - **Expected Results**:
     - ✅ API call to `/api/ManufactureOrder` succeeds (200 OK)
     - ✅ No JavaScript errors in console
     - ✅ Historical orders appear in the list
     - ✅ All orders show `ManufactureType` field (should be "MultiPhase" for old orders)

4. **Check API Response Structure**
   - In Network tab, click on the `/api/ManufactureOrder` request
   - View response JSON
   - For each historical order, verify:
     - ✅ `id` field present
     - ✅ `orderNumber` field present
     - ✅ `manufactureType` field present (value should be 0 or "MultiPhase")
     - ✅ `plannedDate` field present (unified date)
     - ✅ `state` field present
     - ✅ `semiProduct` field present (may be null for some states)
     - ✅ `products` array present

5. **Apply Filters to Historical Orders**
   - Try filtering by:
     - Date range covering historical orders
     - Order state (if historical orders exist in various states)
     - Order number search
   - **Expected Results**:
     - ✅ Filters work correctly with historical orders
     - ✅ No errors when filtering
     - ✅ Historical orders correctly matched by filters

---

### Scenario 2: View Historical Order Details

#### Test Objective
Verify that individual historical order details can be viewed without errors and all data displays correctly.

#### Test Steps

1. **Open Historical Order Detail**
   - From the manufacturing orders list, click on a historical order
   - Record the order being tested: `_______________`
   - **Expected Results**:
     - ✅ Detail page loads without errors
     - ✅ Order header information displays correctly
     - ✅ Order number visible: `_______________`
     - ✅ Creation date visible: `_______________`
     - ✅ Responsible person visible (if assigned)

2. **Verify Order Type Display**
   - Check that manufacture type is displayed
   - **Expected Results**:
     - ✅ Manufacture type shown as "Multi-Phase" or equivalent
     - ✅ No "undefined" or null values displayed
     - ✅ UI correctly interprets `ManufactureType = 0` as MultiPhase

3. **Verify Schedule Information**
   - Check planned date field
   - **Expected Results**:
     - ✅ Planned date displays correctly
     - ✅ Date format is consistent with other orders
     - ✅ No date parsing errors
     - ✅ Date value matches expected historical data

4. **Verify Semi-Product Information**
   - Check semi-product section
   - **Expected Results**:
     - ✅ Semi-product code displays: `_______________`
     - ✅ Semi-product name displays: `_______________`
     - ✅ Planned quantity displays: `_______________`
     - ✅ Actual quantity displays (if completed): `_______________`
     - ✅ Batch multiplier displays: `_______________`
     - ✅ Lot number displays (if set): `_______________`
     - ✅ Expiration date displays (if set): `_______________`

5. **Verify Products Information**
   - Check products section
   - For each product in the order:
     - ✅ Product code displays: `_______________`
     - ✅ Product name displays: `_______________`
     - ✅ Planned quantity displays: `_______________`
     - ✅ Actual quantity displays (if completed): `_______________`

6. **Verify Order State and History**
   - Check order state information
   - **Expected Results**:
     - ✅ Current state displays correctly: `_______________`
     - ✅ State changed at timestamp: `_______________`
     - ✅ State changed by user: `_______________`
     - ✅ Manual action flag (if applicable)

7. **Verify ERP Document References**
   - Check ERP integration fields
   - **Expected Results**:
     - ✅ ERP semi-product order number (if exists): `_______________`
     - ✅ ERP product order number (if exists): `_______________`
     - ✅ ERP discard residue document (if exists): `_______________`
     - ✅ All dates associated with ERP documents display correctly

8. **Verify Notes Section**
   - Check order notes
   - **Expected Results**:
     - ✅ All historical notes visible
     - ✅ Note timestamps correct: `_______________`
     - ✅ Note authors visible: `_______________`
     - ✅ Note text readable and complete

---

### Scenario 3: Historical Orders in Calendar View

#### Test Objective
Verify that historical orders display correctly in calendar/timeline views.

#### Test Steps

1. **Navigate to Calendar View**
   - From manufacturing module, access calendar view
   - Set date range to include historical orders
   - Date range: From `_______________` To `_______________`

2. **Verify Historical Orders Visible**
   - Locate historical orders on calendar
   - **Expected Results**:
     - ✅ Historical orders appear on calendar
     - ✅ Orders positioned on correct dates (using PlannedDate)
     - ✅ Order cards display basic information
     - ✅ No rendering errors or blank cards

3. **Verify Calendar Event Details**
   - Click on a historical order in calendar view
   - **Expected Results**:
     - ✅ Event popup/details appear
     - ✅ Order number visible
     - ✅ Manufacture type visible (MultiPhase)
     - ✅ State visible
     - ✅ Quick actions available (if applicable)

4. **Test Date-Based Filtering**
   - Try navigating to different months/weeks
   - **Expected Results**:
     - ✅ Historical orders appear in correct time periods
     - ✅ Date calculations work correctly with unified PlannedDate
     - ✅ No date-related JavaScript errors

---

### Scenario 4: Historical Orders in Reports

#### Test Objective
Verify that historical orders are included in manufacturing reports and analytics.

#### Test Steps

1. **Generate Manufacturing Report**
   - Access reports/analytics section
   - Generate report covering time period with historical orders
   - Report period: From `_______________` To `_______________`

2. **Verify Historical Data Inclusion**
   - Check report contents
   - **Expected Results**:
     - ✅ Historical orders included in report
     - ✅ Order counts correct
     - ✅ State distributions include historical orders
     - ✅ Date-based aggregations work correctly

3. **Verify Manufacturing Metrics**
   - Check calculated metrics
   - **Expected Results**:
     - ✅ Production volumes include historical data
     - ✅ Material consumption includes historical orders
     - ✅ Completion rates calculated correctly
     - ✅ No calculation errors due to schema changes

4. **Test Report Exports** (if applicable)
   - Export report to CSV/Excel
   - **Expected Results**:
     - ✅ Historical orders present in export
     - ✅ All fields export correctly
     - ✅ Date fields formatted properly
     - ✅ ManufactureType field exports as "MultiPhase"

---

### Scenario 5: Historical Order Operations

#### Test Objective
Verify that operations on historical orders work correctly (view-only expected, modifications may be restricted).

#### Test Steps

1. **Test View Operations**
   - Open historical order detail
   - Try to view all sections
   - **Expected Results**:
     - ✅ All view operations work
     - ✅ No "undefined" fields
     - ✅ No broken references
     - ✅ All related data loads correctly

2. **Test Notes Operations** (if allowed)
   - Try to add a note to historical order
   - **Expected Results**:
     - ✅ Note can be added (if feature allows)
     - ✅ Historical notes remain intact
     - ✅ No corruption of historical data

3. **Verify Read-Only Status** (if applicable)
   - Check if historical orders have any restrictions
   - **Expected Results**:
     - ✅ Appropriate restrictions indicated in UI
     - ✅ Critical fields protected from modification
     - ✅ Clear messaging about order status

---

### Scenario 6: Edge Cases and Boundary Conditions

#### Test Objective
Test edge cases and potential problem scenarios with historical data.

#### Test Steps

1. **Test Orders Around Migration Date**
   - Find orders created close to migration date (around 2025-10-20)
   - Record order numbers:
     - Before migration: `_______________`
     - After migration: `_______________`
   - **Expected Results**:
     - ✅ Both orders query successfully
     - ✅ No date boundary issues
     - ✅ Type field correctly set for both

2. **Test Completed Historical Orders**
   - Find historical order in "Completed" state
   - Order number: `_______________`
   - **Expected Results**:
     - ✅ All completion data visible
     - ✅ Actual quantities display correctly
     - ✅ ERP document numbers present
     - ✅ State history intact

3. **Test In-Progress Historical Orders**
   - Find historical order in any in-progress state
   - Order number: `_______________`
   - State: `_______________`
   - **Expected Results**:
     - ✅ Current state displays correctly
     - ✅ Progress indicators work
     - ✅ Next steps visible (if applicable)

4. **Test Historical Orders with No Semi-Product** (if any exist)
   - Find orders where SemiProduct might be null
   - **Expected Results**:
     - ✅ Null semi-product handled gracefully
     - ✅ No null reference errors
     - ✅ UI shows appropriate message

---

## Expected Results Summary

### Acceptance Criteria Verification

- [ ] ✅ Old manufacturing orders queryable via API
- [ ] ✅ Historical data displays correctly in UI
- [ ] ✅ No errors when viewing old orders
- [ ] ✅ Historical reports continue to work
- [ ] ✅ Calendar views show historical orders correctly
- [ ] ✅ All date fields display correctly (unified PlannedDate)
- [ ] ✅ ManufactureType field shows "MultiPhase" for old orders
- [ ] ✅ No database errors or migration issues

---

## Test Data Recording

### Historical Orders Tested

| Order Number | Creation Date | State | ManufactureType | Test Result | Issues Found |
|--------------|---------------|-------|-----------------|-------------|--------------|
| | | | MultiPhase | | |
| | | | MultiPhase | | |
| | | | MultiPhase | | |

### Verification Checklist

#### API Layer
- [ ] GET /api/ManufactureOrder returns historical orders
- [ ] GET /api/ManufactureOrder/{id} works for historical orders
- [ ] API response contains ManufactureType field
- [ ] API response contains PlannedDate field
- [ ] Filtering works with historical orders

#### UI Layer
- [ ] List view displays historical orders
- [ ] Detail view loads for historical orders
- [ ] All fields render without errors
- [ ] ManufactureType displays as "MultiPhase"
- [ ] PlannedDate displays correctly

#### Calendar/Reports
- [ ] Calendar view shows historical orders
- [ ] Reports include historical data
- [ ] Date calculations correct
- [ ] Metrics include historical orders

---

## Migration Verification

### Database Schema Verification

The migration `20251020081944_AddSinglePhaseManufacturing` made the following changes that affect historical orders:

**Schema Changes**:
1. Added `ManufactureType` column (default: 0 = MultiPhase)
2. Added `PlannedDate` column (date)
3. Removed `SemiProductPlannedDate` column
4. Removed `ProductPlannedDate` column

**Data Migration**:
```sql
-- This SQL was executed during migration:
UPDATE "ManufactureOrders" SET "PlannedDate" = "SemiProductPlannedDate"
```

**Verification Points**:
- [ ] All historical orders have `ManufactureType = 0` (MultiPhase)
- [ ] All historical orders have valid `PlannedDate` values
- [ ] PlannedDate values match original SemiProductPlannedDate
- [ ] No NULL values in ManufactureType or PlannedDate for historical records

---

## Common Issues to Watch For

### Issue 1: Missing ManufactureType Field
**Problem**: Historical orders missing ManufactureType in API response
**Expected**: All orders have ManufactureType = 0 (MultiPhase)
**Severity**: Critical

### Issue 2: Date Field Null or Invalid
**Problem**: PlannedDate shows as null or invalid date
**Expected**: PlannedDate populated from SemiProductPlannedDate
**Severity**: Critical

### Issue 3: UI Doesn't Handle MultiPhase
**Problem**: UI shows "undefined" or errors for ManufactureType = 0
**Expected**: UI displays "Multi-Phase" or equivalent
**Severity**: High

### Issue 4: Calendar Positioning Incorrect
**Problem**: Historical orders appear on wrong dates in calendar
**Expected**: Orders positioned using PlannedDate (formerly SemiProductPlannedDate)
**Severity**: Medium

### Issue 5: Filtering Excludes Historical Orders
**Problem**: Filters don't match historical orders correctly
**Expected**: All filters work with historical data
**Severity**: High

---

## Implementation Reference

### Key Components

- **Migration**: `20251020081944_AddSinglePhaseManufacturing.cs` (backend/src/Anela.Heblo.Persistence/Migrations/)
- **Entity**: `ManufactureOrder.cs` (backend/src/Anela.Heblo.Domain/Features/Manufacture/)
- **Enum**: `ManufactureType.cs` (backend/src/Anela.Heblo.Domain/Features/Manufacture/)
- **Handler**: `GetManufactureOrdersHandler.cs` (backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrders/)
- **DTO**: `ManufactureOrderDto` (backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrders/)

### Migration Context

**Date of Migration**: October 20, 2025

**Historical Orders**: Any manufacturing order created before this date should have:
- `ManufactureType = 0` (MultiPhase) - set by migration default
- `PlannedDate` copied from `SemiProductPlannedDate`

**New Orders**: Orders created after migration can have:
- `ManufactureType = 0` (MultiPhase) OR
- `ManufactureType = 1` (SinglePhase)

---

## Notes for Tester

1. **Staging Environment**: Uses real database with historical data
2. **Migration Applied**: Migration was applied to staging, so historical orders exist
3. **No Rollback**: This is a one-way migration with data transformation
4. **Focus on Reads**: This test focuses on read operations, not modifications
5. **Database Inspector**: If database access available, verify schema directly
6. **Related Stories**:
   - US-004: Consumption movement implementation
   - US-005: Production movement implementation
   - US-015: Manual testing for new flow

---

## Issues Found

| Issue # | Severity | Description | Steps to Reproduce | Expected Behavior | Actual Behavior |
|---------|----------|-------------|-------------------|-------------------|-----------------|
| | | | | | |

---

## Test Sign-off

**Tester Name**: _______________

**Date Completed**: _______________

**Overall Result**: [ ] PASS [ ] FAIL

**Acceptance Criteria Met**:
- [ ] Old manufacturing orders queryable
- [ ] Historical data displays correctly
- [ ] No errors when viewing old orders
- [ ] Historical reports continue to work

**Migration Verification**:
- [ ] ManufactureType field present and correct
- [ ] PlannedDate field present and correct
- [ ] No database errors observed
- [ ] Data integrity maintained

**Comments**:
_______________________________________________________________________________
_______________________________________________________________________________
_______________________________________________________________________________

**Issues Requiring Fix**: [ ] YES [ ] NO

**Issue Tracker IDs**: _______________

**Approved for Next Stage**: [ ] YES [ ] NO

**Approver Name**: _______________

**Approval Date**: _______________
