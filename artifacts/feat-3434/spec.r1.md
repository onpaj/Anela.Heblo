# Spec: Extract `MapToDto` Factory Method in `FinancialAnalysisService`

**Feature ID:** feat-3434
**Module:** FinancialOverview
**Type:** Refactoring / DRY fix
**Filed:** 2026-06-30

---

## Summary

Extract a single private static `MapToDto` factory method in `FinancialAnalysisService` to replace three near-identical inline `MonthlyFinancialDataDto` construction blocks. No behavior changes; this is a pure structural refactoring.

---

## Background

`FinancialAnalysisService` maintains three code paths that each produce a `MonthlyFinancialDataDto`:

| Method | Lines (current) | Path type |
|---|---|---|
| `GetHybridWithCurrentMonthAsync` | 303–321 | Real-time current month |
| `GetCachedFinancialOverview` | 381–398 | Memory-cache read for completed months |
| `GetFinancialOverviewRealTimeAsync` | 514–535 | Full real-time calculation per month |

All three blocks construct the same object shape with identical conditional logic:

- `FinancialBalance` is always `income - expenses`.
- `StockChanges` (a `StockChangeDto`) is populated only when `includeStockData && stockChange != null`; the three fields mapped are always `Materials`, `SemiProducts`, `Products`.
- `TotalStockValueChange` is `null` when `!includeStockData`, otherwise `stockChange?.TotalStockValueChange ?? 0`.
- `TotalBalance` is `null` when `!includeStockData`, otherwise `financialBalance + (stockChange?.TotalStockValueChange ?? 0)`.
- `MonthYearDisplay` is always `$"{month:D2}/{year}"` — though the cached path currently reads `cachedFinancialData.MonthYearDisplay` (which is set to the same format by the domain object). After extraction the factory will generate this consistently.

The duplication crosses the "three similar lines" DRY threshold in the project guidelines. Any future change to the DTO shape or stock-inclusion logic requires three synchronized edits; missing one creates a silent data inconsistency between the cached, hybrid, and real-time paths.

The service already contains two existing private static `CreateStockSummary` overloads, establishing the pattern of private static factory/helper methods in this class. The new method follows that pattern.

---

## Functional Requirements

### FR-1: New private static factory method

Add a private static method to `FinancialAnalysisService` with the following signature:

```csharp
private static MonthlyFinancialDataDto MapToDto(
    int year,
    int month,
    decimal income,
    decimal expenses,
    MonthlyStockChange? stockChange,
    bool includeStockData)
```

**Body (canonical implementation):**

```csharp
{
    var financialBalance = income - expenses;
    return new MonthlyFinancialDataDto
    {
        Year = year,
        Month = month,
        MonthYearDisplay = $"{month:D2}/{year}",
        Income = income,
        Expenses = expenses,
        FinancialBalance = financialBalance,
        StockChanges = includeStockData && stockChange != null
            ? new StockChangeDto
            {
                Materials = stockChange.StockChanges.Materials,
                SemiProducts = stockChange.StockChanges.SemiProducts,
                Products = stockChange.StockChanges.Products
            }
            : null,
        TotalStockValueChange = includeStockData
            ? (stockChange?.TotalStockValueChange ?? 0)
            : null,
        TotalBalance = includeStockData
            ? financialBalance + (stockChange?.TotalStockValueChange ?? 0)
            : null
    };
}
```

### FR-2: Replace call-site in `GetHybridWithCurrentMonthAsync` (lines 303–321)

Replace:
```csharp
var currentMonthDto = new MonthlyFinancialDataDto
{
    Year = now.Year,
    Month = now.Month,
    MonthYearDisplay = $"{now.Month:D2}/{now.Year}",
    Income = income,
    Expenses = expenses,
    FinancialBalance = financialBalance,
    StockChanges = includeStockData && stockChange != null ? new StockChangeDto { ... } : null,
    TotalStockValueChange = includeStockData ? (stockChange?.TotalStockValueChange ?? 0) : null,
    TotalBalance = includeStockData ? financialBalance + (stockChange?.TotalStockValueChange ?? 0) : null
};
```

With:
```csharp
var currentMonthDto = MapToDto(now.Year, now.Month, income, expenses, stockChange, includeStockData);
```

Note: the local variable `financialBalance` (computed just before the construction block) is no longer needed at the call-site — remove it; `MapToDto` computes it internally.

### FR-3: Replace call-site in `GetCachedFinancialOverview` (lines 381–398)

Replace the `monthlyData.Add(new MonthlyFinancialDataDto { ... })` block with:
```csharp
monthlyData.Add(MapToDto(
    cachedFinancialData.Year,
    cachedFinancialData.Month,
    cachedFinancialData.Income,
    cachedFinancialData.Expenses,
    cachedStockData,
    includeStockData));
```

Note: `MonthYearDisplay` was previously read from `cachedFinancialData.MonthYearDisplay`. `MapToDto` generates it as `$"{month:D2}/{year}"`. Both produce the same value (the domain object formats identically); the generated form is authoritative going forward.

### FR-4: Replace call-site in `GetFinancialOverviewRealTimeAsync` (lines 517–535)

Replace the LINQ `.Select(d => new MonthlyFinancialDataDto { ... })` projection lambda body with:
```csharp
.Select(d =>
{
    var stockChangeData = stockChangesLookup.TryGetValue(new { d.Year, d.Month }, out var stockChange)
        ? stockChange
        : null;
    return MapToDto(d.Year, d.Month, d.Income, d.Expenses, stockChangeData, includeStockData);
})
```

### FR-5: Placement

Place `MapToDto` immediately after the two existing `CreateStockSummary` methods (currently at lines 554–586), keeping all private static helpers together.

---

## Non-Functional Requirements

### NFR-1: No behavioral change

The output produced by all three code paths must be byte-for-byte identical to what they produced before the refactoring. Reviewers should confirm this by inspection; no runtime behavior differs.

### NFR-2: Build passes

`dotnet build` must complete without errors or new warnings.

### NFR-3: Format passes

`dotnet format` must produce no diff after the change.

### NFR-4: Existing tests pass

All existing unit and integration tests for the `FinancialOverview` module must continue to pass without modification.

### NFR-5: Surgical scope

Only `FinancialAnalysisService.cs` is changed. No other files are touched.

---

## Data Model

No changes to any DTO or domain entity. The classes involved are read-only from this refactoring's perspective:

| Class | File | Role |
|---|---|---|
| `MonthlyFinancialDataDto` | `Model/MonthlyFinancialDataDto.cs` | Target of construction; unchanged |
| `StockChangeDto` | `Model/StockChangeDto.cs` | Nested DTO; unchanged |
| `MonthlyStockChange` | Domain | Source data; unchanged |

---

## API / Interface Design

No public API surface changes. `MapToDto` is `private static` and has no effect on:

- Controller endpoints
- MediatR handler contracts
- `IFinancialAnalysisService` interface
- OpenAPI schema / TypeScript client

---

## Affected File

**Single file changed:**

```
backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```

**Change summary:**

| Action | Details |
|---|---|
| Add | `private static MonthlyFinancialDataDto MapToDto(...)` method (~20 lines) |
| Remove | Inline `new MonthlyFinancialDataDto { ... }` block at lines 303–321 (~19 lines) |
| Remove | Inline `new MonthlyFinancialDataDto { ... }` block at lines 381–398 (~19 lines) |
| Remove | Inline `new MonthlyFinancialDataDto { ... }` block at lines 517–535 (~19 lines) |
| Net delta | ~−37 lines (3 × 19 removed, ~20 added) |

---

## Dependencies

None. This is a self-contained change within a single class. No new packages, no new interfaces, no configuration changes.

---

## Out of Scope

- Changes to any other method in `FinancialAnalysisService`.
- Changes to `CreateStockSummary` or `CalculatePeriodTotals`.
- Changes to DTOs, domain entities, handlers, controllers, or tests.
- Adding or removing `FinancialBalance` caching behavior in the cached path.
- Any refactoring of `MonthlyFinancialData` (the internal domain object used in the real-time path before projection to DTO).
- New unit tests for `MapToDto` (the method is private and fully covered indirectly by existing tests for the three public paths).

---

## Open Questions

None.

---

## Status: COMPLETE
