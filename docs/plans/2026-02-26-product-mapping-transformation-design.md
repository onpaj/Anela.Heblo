# Product Code Mapping Transformation - Design Document

**Date:** 2026-02-26
**Status:** Approved
**Component:** `ProductMappingIssuedInvoiceImportTransformation`

## Overview

This document describes the implementation of the `TransformAsync` method in `ProductMappingIssuedInvoiceImportTransformation` to enable product code replacement during invoice import.

## Purpose

Replace specific product codes during invoice import to handle product code migrations or corrections. The current configuration maps legacy code "1287" to new code "SLU000001".

## Architecture & Integration

### Integration Point
- Implements `IIssuedInvoiceImportTransformation` interface
- Registered in DI container via `InvoicesModule.AddInvoicesModule()`
- Part of transformation pipeline that processes invoices during import from FlexiBee

### Scope
- Limited to product code replacement in invoice line items (`IssuedInvoiceDetailItem.Code`)
- Does not modify other invoice properties (prices, quantities, addresses, etc.)

## Implementation Details

### Algorithm
1. Iterate through all items in `invoiceDetail.Items`
2. For each item, check if `item.Code` exactly matches `_originalProductCode`
3. If match found, replace `item.Code` with `_newProductCode`
4. Continue through all items (replace all occurrences)
5. Return the modified `invoiceDetail`

### Code Changes
- **File:** `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformation.cs`
- **Method:** `TransformAsync` (line 17)
- **Implementation:** Simple foreach loop with exact string comparison

### Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Match Type** | Exact match only | Only replace when `item.Code == _originalProductCode` (not partial/substring). Prevents unintended replacements. |
| **Case Sensitivity** | Case-sensitive | Uses default string equality. Product codes are case-sensitive identifiers. |
| **Mutation** | Modifies original | Mutates the input object directly (no cloning). More efficient, aligns with transformation pattern. |
| **Match Scope** | Replace all occurrences | Processes all items in the invoice, not just first match. Ensures complete transformation. |

### Code Implementation

```csharp
public Task<IssuedInvoiceDetail> TransformAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
{
    foreach (var item in invoiceDetail.Items)
    {
        if (item.Code == _originalProductCode)
        {
            item.Code = _newProductCode;
        }
    }

    return Task.FromResult(invoiceDetail);
}
```

## Error Handling & Edge Cases

### Edge Cases Handled
1. **Empty items list**: Foreach loop handles empty collections naturally (no iterations, no errors)
2. **No matches found**: Transformation completes successfully, returns original invoice unchanged
3. **Null items collection**: Will throw `NullReferenceException` - but shouldn't happen as `Items` is initialized to `new()` in domain model
4. **Null/empty product codes**: Constructor accepts any string values, comparison works correctly

### Error Handling Strategy
- **No explicit validation**: Transformation is straightforward; standard .NET exception handling suffices
- **No logging**: Data transformation doesn't need audit trails
- **Trust domain model**: Assume `IssuedInvoiceDetail` is always in valid state

### YAGNI - What We're NOT Doing
- Not validating that original/new codes are non-empty
- Not logging when replacements occur
- Not counting/returning how many items were changed
- Not checking if new product code exists in catalog

## Testing Strategy

### Unit Tests

**Test Location:** `backend/src/Anela.Heblo.Application.Tests/Features/Invoices/Infrastructure/Transformations/ProductMappingIssuedInvoiceImportTransformationTests.cs`

**Test Cases:**
1. **Happy path**: Invoice with one item matching the original code → code gets replaced
2. **Multiple matches**: Invoice with multiple items with same code → all get replaced
3. **No matches**: Invoice with items that don't match → nothing changes
4. **Empty items**: Invoice with empty items list → no errors, returns unchanged
5. **Mixed items**: Invoice with some matching, some non-matching → only matching items replaced

**Test Approach:**
- Create test `IssuedInvoiceDetail` objects with various item configurations
- Execute `TransformAsync`
- Assert that correct items were modified and others left untouched

### Integration Testing
- Not required - this is a pure data transformation with no external dependencies
- Integration tests at the invoice import service level will cover this naturally

## Implementation Plan

See separate implementation plan document for detailed step-by-step instructions.
