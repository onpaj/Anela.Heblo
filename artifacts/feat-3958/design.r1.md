# Design: ManufactureCost unit tests

## Component Design
Single test class `ManufactureCostTests` in namespace `Anela.Heblo.Tests.Features.Catalog`, file `backend/test/Anela.Heblo.Tests/Features/Catalog/ManufactureCostTests.cs`, using `Anela.Heblo.Domain.Features.Catalog`, `FluentAssertions`, `Xunit`.

A private helper `Create(DateTime date)` returns a `ManufactureCost` with `MaterialCostFromReceiptDocument = 100m`, `MaterialCostFromPurchasePrice = 60m`, `HandlingCost = 7m`.

Tests:
- `MaterialCost_BeforeCutoff_UsesPurchasePrice` Theory: (2024,1,1), (2025,8,24) -> 60.
- `MaterialCost_AfterCutoff_UsesReceiptDocument` Theory: (2025,8,26), (2026,1,1) -> 100.
- `MaterialCost_ExactlyAtCutoffMidnight_UsesPurchasePrice` Fact: 2025-08-25 00:00 -> 60.
- `MaterialCost_SameDayAfterMidnight_UsesReceiptDocument` Fact: 2025-08-25 12:00 -> 100.
- `MaterialCost_LastSecondBeforeCutoffDay_UsesPurchasePrice` Fact: 2025-08-24 23:59:59 -> 60.
- `Total_BeforeCutoff_SumsPurchasePriceAndHandling` -> 67.
- `Total_AfterCutoff_SumsReceiptDocumentAndHandling` -> 107.
- `Total_DefaultInstance_IsZero` -> 0.

## Data Schemas
None (no schema, API or contract changes).
