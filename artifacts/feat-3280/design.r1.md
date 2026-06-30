# Design: CreatePurchaseOrderRequestValidatorTests

## Test structure

Single file: `CreatePurchaseOrderRequestValidatorTests.cs`

Two test classes:
1. `CreatePurchaseOrderRequestValidatorTests` — covers the outer validator
2. `CreatePurchaseOrderLineRequestValidatorTests` — covers the line-item sub-validator

## Conventions
- `[Fact]` for single-case tests, `[Theory]` + `[InlineData]` for parameterized cases
- `TestValidate()` from `FluentValidation.TestHelper`
- `ShouldHaveValidationErrorFor` / `ShouldNotHaveValidationErrorFor` for field-level assertions
- `ShouldNotHaveAnyValidationErrors()` for happy-path full-request tests
- Date-relative helpers use `DateTime.UtcNow` to avoid flakiness

## Date helpers (static, private)
```csharp
private static string TodayStr => DateTime.UtcNow.ToString("yyyy-MM-dd");
private static string FutureStr(int days) => DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd");
private static string PastStr(int days) => DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
```

## Test groupings

### CreatePurchaseOrderRequestValidatorTests
- `SupplierId_*`
- `OrderDate_*`
- `ExpectedDeliveryDate_*`
- `Notes_*`
- `OrderNumber_*`
- `Lines_*`
- `ValidRequest_PassesAllValidation`

### CreatePurchaseOrderLineRequestValidatorTests
- `MaterialId_*`
- `Quantity_*`
- `UnitPrice_*`
- `Notes_*`
- `ValidLine_PassesAllValidation`
