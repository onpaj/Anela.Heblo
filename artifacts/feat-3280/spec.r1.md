# Spec: Unit Tests for CreatePurchaseOrderRequestValidator

## Feature ID
feat-3280

## Problem
`CreatePurchaseOrderRequestValidator` and `CreatePurchaseOrderLineRequestValidator` have no unit tests. This is a coverage gap in the Purchase module.

## Goal
Add unit tests that verify all validation rules for both validators, using FluentValidation.TestHelper and the project's standard xUnit + FluentAssertions pattern.

## Scope
- Backend only — no UI or API contract changes.
- New test file: `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderRequestValidatorTests.cs`
- No changes to production code.

## Rules to cover

### CreatePurchaseOrderRequestValidator
| Field | Rule | Cases |
|---|---|---|
| SupplierId | GreaterThan(0) | 0 → error, -1 → error, 1 → pass |
| OrderDate | NotEmpty + valid date | null/empty → error, invalid string → error |
| OrderDate | NotBeTooFarInFuture | today → pass, +30d → pass, +31d → error |
| ExpectedDeliveryDate | optional; valid date | null → pass, invalid string → error |
| ExpectedDeliveryDate | >= OrderDate | before OrderDate → error, same → pass, after → pass |
| Notes | MaximumLength(1000) | 1000 chars → pass, 1001 → error |
| OrderNumber | MaximumLength(50) | 50 chars → pass, 51 → error |
| Lines | Count <= 100 | null → pass, 0 → pass, 100 → pass, 101 → error |

### CreatePurchaseOrderLineRequestValidator
| Field | Rule | Cases |
|---|---|---|
| MaterialId | NotEmpty + MaxLength(50) | empty → error, 50 chars → pass, 51 → error |
| Quantity | GreaterThan(0) + <= 999999.99 | 0 → error, negative → error, 0.01 → pass, 999999.99 → pass, 1000000 → error |
| UnitPrice | >= 0 + <= 999999.99 | -0.01 → error, 0 → pass, 999999.99 → pass, 1000000 → error |
| Notes | MaximumLength(500) | null → pass, 500 chars → pass, 501 → error |
