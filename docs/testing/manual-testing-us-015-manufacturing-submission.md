# Manual Testing Guide: US-015 - Manufacturing Submission

**User Story**: US-015 - Manual testing: Submit manufacturing with valid materials

**Test Date**: 2026-02-03

**Tester**: Manual QA

**Environment**: Staging (https://heblo-test-staging.azurewebsites.net)

---

## Overview

This document provides a step-by-step manual testing procedure to verify the manufacturing submission functionality against the FlexiBee staging environment.

The manufacturing submission creates **two stock movements**:
1. **Consumption Movement** (OUT) - Removes raw materials from material warehouse
2. **Production Movement** (IN) - Adds finished goods to target warehouse (semi-products or products)

---

## Prerequisites

- Access to staging environment: https://heblo-test-staging.azurewebsites.net
- Microsoft Entra ID credentials for authentication
- Access to FlexiBee staging environment to verify stock movements
- Knowledge of FlexiBee document codes for verification

---

## Test Scenarios

### Scenario 1: Successful Manufacturing Submission (Happy Path)

#### Test Objective
Verify that manufacturing submission successfully creates both consumption and production movements in FlexiBee staging.

#### Test Steps

1. **Navigate to Manufacturing Module**
   - Login to staging environment: https://heblo-test-staging.azurewebsites.net
   - Navigate to the Manufacturing page

2. **Select Manufacturing Order**
   - Choose an existing manufacturing order OR create a new one
   - Record the manufacturing order code: `_______________`

3. **Verify Material Availability**
   - Before submission, note the current stock quantities:
     - Material 1 Code: `_______________` Current Stock: `_______________`
     - Material 2 Code: `_______________` Current Stock: `_______________`
     - (Add more materials as needed)

4. **Submit Manufacturing Order**
   - Click "Submit Manufacturing" button
   - Enter required details:
     - Date: `_______________`
     - Lot Number (if applicable): `_______________`
     - Expiration Date (if applicable): `_______________`
   - Confirm submission

5. **Verify Success Response**
   - ✅ Success message displayed
   - ✅ Manufacturing ID returned: `_______________`
   - ✅ No error messages shown

6. **Verify Consumption Movement in FlexiBee**
   - Login to FlexiBee staging environment
   - Navigate to Stock Movements
   - Search for consumption movement by manufacturing order code
   - **Expected Results**:
     - ✅ Consumption movement exists
     - ✅ Movement direction: OUT
     - ✅ Document type: `VYROBA-POLOTOVAR` or `VYROBA-PRODUKT`
     - ✅ Warehouse: Material warehouse (ID: based on FlexiStockClient)
     - ✅ Document code recorded: `_______________`
     - ✅ All materials listed with correct quantities
     - ✅ Material quantities match submission request

7. **Verify Production Movement in FlexiBee**
   - Still in FlexiBee, search for production movement
   - Search by manufacturing order code
   - **Expected Results**:
     - ✅ Production movement exists
     - ✅ Movement direction: IN
     - ✅ Document type: `VYROBA-POLOTOVAR` or `VYROBA-PRODUKT`
     - ✅ Warehouse: Semi-products warehouse OR Products warehouse
     - ✅ Document code recorded: `_______________`
     - ✅ Finished goods listed with correct quantities

8. **Verify Material Quantities Updated**
   - Navigate to Material Stock report in FlexiBee
   - Check stock quantities for consumed materials
   - **Expected Results**:
     - ✅ Material 1 stock reduced by consumption amount
     - ✅ Material 2 stock reduced by consumption amount
     - ✅ New stock quantities match expected values

9. **Verify Finished Goods Added to Inventory**
   - Navigate to appropriate warehouse (semi-products or products)
   - Check stock for manufactured items
   - **Expected Results**:
     - ✅ Finished goods stock increased by production amount
     - ✅ Lot number correctly assigned (if applicable)
     - ✅ Expiration date correctly set (if applicable)

---

### Scenario 2: Verify Logging and Traceability

#### Test Objective
Verify that manufacturing submission creates proper logging and audit trail.

#### Test Steps

1. **Check Application Logs**
   - Access application logs for staging environment
   - Search for manufacturing submission logs
   - **Expected Log Entries**:
     - ✅ "Starting manufacture consumption movement" log exists
     - ✅ Manufacturing order code logged
     - ✅ "Successfully created consumption movement {MovementReference}" log exists
     - ✅ "Starting production movement creation" log exists
     - ✅ "Successfully created both consumption movement {ConsumptionRef} and production movement {ProductionRef}" log exists

2. **Verify Movement Linkage**
   - In FlexiBee, verify both movements reference same manufacturing order
   - **Expected Results**:
     - ✅ Both movements have same description (manufacturing order code)
     - ✅ Both movements have same note (manufacturing internal number)
     - ✅ Movements are traceable to original manufacturing order

---

### Scenario 3: Verify Different Manufacture Types

#### Test Objective
Verify that both semi-product and finished product manufacturing work correctly.

#### Semi-Product Manufacturing

1. **Submit Semi-Product Manufacturing**
   - Select manufacturing order for semi-product
   - Submit order
   - **Expected Results**:
     - ✅ Consumption movement uses document type: `VYROBA-POLOTOVAR`
     - ✅ Production movement uses document type: `VYROBA-POLOTOVAR`
     - ✅ Production movement targets semi-products warehouse

#### Finished Product Manufacturing

1. **Submit Finished Product Manufacturing**
   - Select manufacturing order for finished product
   - Submit order
   - **Expected Results**:
     - ✅ Consumption movement uses document type: `VYROBA-PRODUKT`
     - ✅ Production movement uses document type: `VYROBA-PRODUKT`
     - ✅ Production movement targets products warehouse

---

## Expected Results Summary

### Acceptance Criteria Verification

- [ ] ✅ Manufacturing submission succeeds
- [ ] ✅ Consumption movement visible in FlexiBee staging
- [ ] ✅ Production movement visible in FlexiBee staging
- [ ] ✅ Material quantities correctly updated
- [ ] ✅ Finished goods correctly added to inventory

---

## Test Data Recording

### Test Execution Record

| Test Case | Manufacturing Order Code | Consumption Movement ID | Production Movement ID | Result | Notes |
|-----------|-------------------------|------------------------|----------------------|--------|-------|
| Happy Path - Semi-Product | | | | | |
| Happy Path - Finished Product | | | | | |

### Issues Found

| Issue # | Severity | Description | Steps to Reproduce | Expected Behavior | Actual Behavior |
|---------|----------|-------------|-------------------|-------------------|-----------------|
| | | | | | |

---

## FlexiBee Verification Checklist

### Consumption Movement Verification

- [ ] Movement exists in FlexiBee
- [ ] Movement direction: OUT
- [ ] Warehouse ID correct (material warehouse)
- [ ] Document type correct
- [ ] All materials listed
- [ ] Material quantities correct
- [ ] Unit prices populated
- [ ] Lot number/expiration date (if applicable)

### Production Movement Verification

- [ ] Movement exists in FlexiBee
- [ ] Movement direction: IN
- [ ] Warehouse ID correct (semi-products or products warehouse)
- [ ] Document type correct
- [ ] Finished goods listed
- [ ] Finished goods quantities correct
- [ ] Unit prices calculated correctly
- [ ] Lot number/expiration date (if applicable)

### Stock Quantity Verification

- [ ] Material stock reduced by correct amounts
- [ ] Finished goods stock increased by correct amounts
- [ ] Stock movements reflect in inventory reports
- [ ] No orphaned or incomplete movements

---

## Implementation Reference

### Key Components Tested

- **Handler**: `SubmitManufactureHandler` (backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs:25)
- **Client**: `FlexiManufactureClient` (backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs:32)
- **Document Types**:
  - Semi-Product: `VYROBA-POLOTOVAR`
  - Finished Product: `VYROBA-PRODUKT`

### Stock Movement Flow

1. **Validation**: Items with amount > 0 validated
2. **Price Lookup**: Current stock prices retrieved for materials
3. **Consumption Movement**: Created with OUT direction, material warehouse
4. **Document Reference**: Consumption movement ID retrieved
5. **Production Movement**: Created with IN direction, target warehouse
6. **Success Response**: Manufacturing ID returned

---

## Notes for Tester

1. **Staging Environment**: Uses real FlexiBee staging API, not mocks
2. **Authentication**: Real Microsoft Entra ID authentication required
3. **Data Persistence**: Changes are persisted in FlexiBee staging
4. **Rollback**: If needed, manually delete movements in FlexiBee (note movement IDs)
5. **Error Scenarios**: For error testing, refer to US-016, US-018

---

## Test Sign-off

**Tester Name**: _______________

**Date Completed**: _______________

**Overall Result**: [ ] PASS [ ] FAIL

**Comments**:
_______________________________________________________________________________
_______________________________________________________________________________
_______________________________________________________________________________

**Approved for Next Stage**: [ ] YES [ ] NO

**Approver Name**: _______________

**Approval Date**: _______________
