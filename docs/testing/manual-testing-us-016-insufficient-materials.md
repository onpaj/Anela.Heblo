# Manual Testing Guide: US-016 - Insufficient Materials Validation

**User Story**: US-016 - Manual testing: Submit with insufficient materials

**Test Date**: 2026-02-03

**Tester**: Manual QA

**Environment**: Staging (https://heblo-test-staging.azurewebsites.net)

---

## Overview

This document provides a step-by-step manual testing procedure to verify that manufacturing submission properly validates material availability and returns clear error messages when materials are insufficient.

**Expected Behavior**:
- Manufacturing submission should fail with validation error
- No stock movements should be created in FlexiBee
- Error message should clearly indicate which material is insufficient
- System should remain in consistent state (no partial transactions)

---

## Prerequisites

- Access to staging environment: https://heblo-test-staging.azurewebsites.net
- Microsoft Entra ID credentials for authentication
- Access to FlexiBee staging environment to verify no movements were created
- Knowledge of current material stock levels in staging

---

## Test Scenarios

### Scenario 1: Single Material Insufficient (Primary Test Case)

#### Test Objective
Verify that manufacturing submission fails with clear error message when a single material has insufficient stock.

#### Test Steps

1. **Identify Material with Low Stock**
   - Login to FlexiBee staging environment
   - Navigate to Material Stock report
   - Identify a material with known low stock quantity
   - Record material details:
     - Material Code: `_______________`
     - Material Name: `_______________`
     - Current Stock: `_______________`

2. **Navigate to Manufacturing Module**
   - Login to staging environment: https://heblo-test-staging.azurewebsites.net
   - Navigate to the Manufacturing page

3. **Create/Select Manufacturing Order**
   - Create or select a manufacturing order that requires the low-stock material
   - Ensure the required quantity exceeds available stock
   - Record manufacturing order details:
     - Manufacturing Order Code: `_______________`
     - Material Code: `_______________`
     - Required Quantity: `_______________`
     - Available Stock: `_______________`
     - Shortage: `_______________`

4. **Attempt Manufacturing Submission**
   - Fill in submission form:
     - Date: `_______________`
     - Lot Number (if applicable): `_______________`
     - Expiration Date (if applicable): `_______________`
   - Click "Submit Manufacturing" button

5. **Verify Error Response**
   - **Expected Results**:
     - ✅ Submission fails (no success message)
     - ✅ Error message displayed clearly
     - ✅ Error specifies material code: `_______________`
     - ✅ Error message indicates insufficient stock
     - ✅ Error message is user-friendly (Czech language expected)
     - ✅ No partial success indicators shown

6. **Verify Error Details**
   - Check error message contains:
     - ✅ Material identification (code or name)
     - ✅ Reason for failure (insufficient stock/quantity)
     - ✅ FlexiBee error details (if available)
   - Record exact error message:
     ```
     _______________________________________________________________________________
     _______________________________________________________________________________
     ```

7. **Verify No Stock Movements Created in FlexiBee**
   - Login to FlexiBee staging environment
   - Navigate to Stock Movements
   - Search for movements by manufacturing order code
   - **Expected Results**:
     - ✅ NO consumption movement created
     - ✅ NO production movement created
     - ✅ Material stock quantity unchanged
     - ✅ No orphaned or incomplete documents

8. **Verify System State Consistency**
   - Check application logs (if accessible)
   - **Expected Log Entries**:
     - ✅ Error logged with manufacturing order code
     - ✅ FlexiBee error message captured in logs
     - ✅ No "Successfully created consumption movement" log
     - ✅ No "Successfully created production movement" log

---

### Scenario 2: Multiple Materials Insufficient

#### Test Objective
Verify behavior when multiple materials have insufficient stock.

#### Test Steps

1. **Identify Multiple Materials with Low Stock**
   - In FlexiBee staging, identify 2+ materials with low stock
   - Record materials:
     - Material 1 Code: `_______________` Stock: `_______________`
     - Material 2 Code: `_______________` Stock: `_______________`

2. **Create Manufacturing Order**
   - Create manufacturing order requiring both materials
   - Ensure required quantities exceed available stock for both
   - Required quantities:
     - Material 1: `_______________` (Available: `_______________`)
     - Material 2: `_______________` (Available: `_______________`)

3. **Attempt Submission**
   - Submit manufacturing order
   - **Expected Results**:
     - ✅ Submission fails with error
     - ✅ Error identifies at least one insufficient material
     - ✅ Error message is clear and actionable

4. **Verify Error Handling**
   - Record which material was identified in error:
     ```
     _______________________________________________________________________________
     ```
   - Note: FlexiBee may fail on first insufficient material encountered

---

### Scenario 3: Edge Case - Exactly Sufficient Stock

#### Test Objective
Verify successful submission when stock exactly matches required quantity.

#### Test Steps

1. **Setup Exact Stock Match**
   - Identify material with known stock quantity
   - Create manufacturing order requiring exact available quantity
   - Material Code: `_______________`
   - Stock Quantity: `_______________`
   - Required Quantity: `_______________` (should equal stock)

2. **Submit Manufacturing**
   - Submit manufacturing order
   - **Expected Results**:
     - ✅ Submission succeeds
     - ✅ Consumption movement created
     - ✅ Production movement created
     - ✅ Material stock reduced to 0

3. **Verify Success**
   - Check FlexiBee for both movements
   - Verify material stock is now 0 or close to 0

---

### Scenario 4: Error Message Localization

#### Test Objective
Verify error messages are properly localized (Czech language expected).

#### Test Steps

1. **Trigger Insufficient Materials Error**
   - Use any manufacturing order with insufficient materials
   - Submit order

2. **Verify Error Message Language**
   - **Expected Results**:
     - ✅ Error message in Czech language (if localization implemented)
     - ✅ OR Error message in English (if localization not yet implemented)
     - ✅ Message is grammatically correct
     - ✅ Message is understandable to end users

3. **Record Error Message Details**
   - Language: `_______________`
   - Message: `_______________`
   - User-friendliness rating (1-5): `_______________`

---

## Expected Results Summary

### Acceptance Criteria Verification

- [ ] ✅ Validation error returned when materials insufficient
- [ ] ✅ Clear error message shown to user
- [ ] ✅ NO stock movements created in FlexiBee
- [ ] ✅ Error message specifies which material is insufficient
- [ ] ✅ System remains in consistent state (no partial transactions)

---

## Error Response Structure

Based on code analysis (SubmitManufactureHandler.cs:58-70), the error response should contain:

**Expected Error Response**:
```json
{
  "errorCode": "CONSUMPTION_MOVEMENT_FAILED",
  "errorDetails": {
    "ManufactureOrderCode": "<order_code>",
    "FlexiBeeError": "<flexibee_error_message>",
    "ErrorMessage": "<user_friendly_message>"
  }
}
```

**Key Fields to Verify**:
- ✅ `errorCode`: Should be "CONSUMPTION_MOVEMENT_FAILED"
- ✅ `ManufactureOrderCode`: Contains correct order code
- ✅ `FlexiBeeError`: Contains FlexiBee validation message
- ✅ `ErrorMessage`: User-friendly error description

---

## Test Data Recording

### Test Execution Record

| Test Case | Manufacturing Order | Material Code | Required Qty | Available Qty | Error Received | Result | Notes |
|-----------|-------------------|---------------|--------------|---------------|----------------|--------|-------|
| Single Insufficient | | | | | | | |
| Multiple Insufficient | | | | | | | |
| Exact Match | | | | | | | |
| Localization Check | | | | | | | |

### Issues Found

| Issue # | Severity | Description | Steps to Reproduce | Expected Behavior | Actual Behavior |
|---------|----------|-------------|-------------------|-------------------|-----------------|
| | | | | | |

---

## FlexiBee Verification Checklist

### After Failed Submission

- [ ] No consumption movement exists in FlexiBee
- [ ] No production movement exists in FlexiBee
- [ ] Material stock quantities unchanged from pre-submission values
- [ ] No orphaned documents in stock movements
- [ ] No partial transactions visible

### After Successful Edge Case (Exact Match)

- [ ] Consumption movement exists and is complete
- [ ] Production movement exists and is complete
- [ ] Material stock correctly reduced to 0
- [ ] Finished goods stock correctly increased

---

## Implementation Reference

### Key Components Tested

- **Handler**: `SubmitManufactureHandler` (backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs:58-70)
- **Client**: `FlexiManufactureClient` (backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs:85-95)
- **Exception**: `ConsumptionMovementFailedException` (backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/Exceptions/)

### Validation Flow

1. **Request Validation**: Items with amount > 0 validated
2. **Stock Price Lookup**: Current stock prices retrieved (StockToDateAsync)
3. **Consumption Movement Creation**: FlexiBee validates material availability
4. **FlexiBee Validation**: FlexiBee returns error if materials insufficient
5. **Error Handling**: ConsumptionMovementFailedException thrown
6. **Error Response**: Structured error returned to frontend
7. **No Rollback Needed**: Consumption movement never created

**Critical Point**: Validation happens at FlexiBee level during consumption movement creation (line 85 in FlexiManufactureClient.cs). If FlexiBee returns error, no movements are created and system remains consistent.

---

## Notes for Tester

1. **FlexiBee Validation**: Material availability is validated by FlexiBee, not application code
2. **Error Messages**: FlexiBee error messages may be technical - verify application wraps them appropriately
3. **No Partial Transactions**: If consumption movement fails, no production movement is attempted
4. **Stock Consistency**: Verify FlexiBee stock reports before and after test
5. **Negative Testing**: This is a negative test - failure is expected behavior
6. **Related Stories**:
   - US-015: Successful submission (happy path)
   - US-018: Production movement failure handling

---

## Common Issues to Watch For

### Issue 1: Generic Error Message
**Problem**: Error message doesn't specify which material is insufficient
**Expected**: Error should identify material code/name
**Severity**: Medium

### Issue 2: Partial Transaction
**Problem**: Consumption movement created but submission failed
**Expected**: No movements created on validation error
**Severity**: Critical

### Issue 3: Unclear Error Message
**Problem**: FlexiBee error message shown directly without user-friendly translation
**Expected**: Technical error wrapped in user-friendly message
**Severity**: Low

### Issue 4: No Material Identification
**Problem**: Error says "insufficient materials" without specifics
**Expected**: Error identifies which material(s) are insufficient
**Severity**: Medium

---

## Test Sign-off

**Tester Name**: _______________

**Date Completed**: _______________

**Overall Result**: [ ] PASS [ ] FAIL

**Acceptance Criteria Met**:
- [ ] Validation error returned
- [ ] Clear error message shown
- [ ] No stock movements created
- [ ] Error specifies insufficient material

**Comments**:
_______________________________________________________________________________
_______________________________________________________________________________
_______________________________________________________________________________

**Issues Requiring Fix**: [ ] YES [ ] NO

**Issue Tracker IDs**: _______________

**Approved for Next Stage**: [ ] YES [ ] NO

**Approver Name**: _______________

**Approval Date**: _______________
