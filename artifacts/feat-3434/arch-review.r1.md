# Architecture Review: Extract MapToDto Factory Method

## Skip Design: true

This is a single-file, single-class refactoring with no behavioral change, no new dependencies, no API surface changes, and no cross-module impact. The pattern already exists in the same class (`CreateStockSummary` overloads). No design decisions are required beyond what the spec already prescribes; the implementation path is unambiguous.

## Architectural Fit Assessment

The change is entirely consistent with the existing codebase patterns:

- `FinancialAnalysisService` already uses private static factory methods (`CreateStockSummary` with two overloads) for exactly this purpose — extracting repeated object-construction logic into a named, reusable helper.
- All three construction blocks produce `MonthlyFinancialDataDto` with identical conditional logic for `StockChanges`, `TotalStockValueChange`, and `TotalBalance`. The duplication is real and the abstraction boundary is correct.
- The method is appropriately `private static`: it has no side effects, no instance state dependency, and is not part of any interface or public contract.
- Clean Architecture module boundaries are not affected. The type stays inside `FinancialOverview/Services/`.

One subtle correctness point worth confirming: in `GetCachedFinancialOverview` (line 385), the `MonthYearDisplay` and `FinancialBalance` fields are sourced from `MonthlyFinancialData` computed properties (`cachedFinancialData.MonthYearDisplay`, `cachedFinancialData.FinancialBalance`). In `GetHybridWithCurrentMonthAsync` (lines 307–310) and `GetFinancialOverviewRealTimeAsync` (lines 521–524), these values are inlined as `$"{now.Month:D2}/{now.Year}"` and `income - expenses` / `d.MonthYearDisplay` / `d.FinancialBalance`. The `MapToDto` signature uses raw `income` and `expenses` primitives, so `FinancialBalance = income - expenses` and `MonthYearDisplay = $"{month:D2}/{year}"` in the method body will reproduce the same values as the domain property computed expressions — this is safe.

## Proposed Architecture

### Component Overview

One new private static method added to `FinancialAnalysisService`:

```
FinancialAnalysisService
  ├── GetHybridWithCurrentMonthAsync       — FR-2: calls MapToDto
  ├── GetCachedFinancialOverview           — FR-3: calls MapToDto
  ├── GetFinancialOverviewRealTimeAsync    — FR-4: calls MapToDto inside LINQ Select
  ├── CreateStockSummary (overload 1)      — existing
  ├── CreateStockSummary (overload 2)      — existing
  └── MapToDto                             — new, FR-1, placed after CreateStockSummary per FR-5
```

No other files, types, or modules are involved.

### Key Design Decisions

**Signature as specified is correct.** `MapToDto(int year, int month, decimal income, decimal expenses, MonthlyStockChange? stockChange, bool includeStockData)` matches what all three call sites need. Using primitives rather than the `MonthlyFinancialData` domain entity keeps the method usable from `GetHybridWithCurrentMonthAsync`, which computes income/expenses directly from ledger items and has no `MonthlyFinancialData` instance.

**`MonthlyStockChange?` nullable parameter.** All three sites already handle a potentially-null stock change. The `?` is essential: in `GetHybridWithCurrentMonthAsync`, `stockChanges.FirstOrDefault()` is already nullable; in the other two paths, stock data may be absent from cache or lookup.

**`includeStockData` boolean guard.** The flag gates whether any stock fields are populated. The spec correctly retains this as an explicit parameter rather than folding it into the null-check on `stockChange`, because a null `stockChange` with `includeStockData = true` should still emit `TotalStockValueChange = 0` and compute `TotalBalance`, whereas `includeStockData = false` should emit `null` for both regardless of stock data availability.

## Implementation Guidance

### Directory / Module Structure

Change is confined to one file:

```
backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs
```

No new files. No moves.

### Interfaces and Contracts

None. `MapToDto` is `private static` — not exposed through any interface, controller, or DTO. The generated OpenAPI TypeScript client is unaffected.

### Data Flow

**FR-2 (GetHybridWithCurrentMonthAsync):**
```
(now.Year, now.Month, income, expenses, stockChanges.FirstOrDefault(), includeStockData)
  → MapToDto → MonthlyFinancialDataDto (currentMonthDto)
```

**FR-3 (GetCachedFinancialOverview):**
```
(cachedFinancialData.Year, cachedFinancialData.Month,
 cachedFinancialData.Income, cachedFinancialData.Expenses,
 cachedStockData, includeStockData)
  → MapToDto → MonthlyFinancialDataDto (element of monthlyData list)
```

**FR-4 (GetFinancialOverviewRealTimeAsync LINQ Select):**
```
(d.Year, d.Month, d.Income, d.Expenses, stockChangeData, includeStockData)
  → MapToDto → MonthlyFinancialDataDto (projected element)
```
where `stockChangeData` is resolved via `stockChangesLookup.TryGetValue(...)` (existing lookup, already nullable).

### MapToDto implementation body

```csharp
private static MonthlyFinancialDataDto MapToDto(
    int year,
    int month,
    decimal income,
    decimal expenses,
    MonthlyStockChange? stockChange,
    bool includeStockData)
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
        StockChanges = includeStockData && stockChange != null ? new StockChangeDto
        {
            Materials = stockChange.StockChanges.Materials,
            SemiProducts = stockChange.StockChanges.SemiProducts,
            Products = stockChange.StockChanges.Products
        } : null,
        TotalStockValueChange = includeStockData ? (stockChange?.TotalStockValueChange ?? 0) : null,
        TotalBalance = includeStockData
            ? financialBalance + (stockChange?.TotalStockValueChange ?? 0)
            : null
    };
}
```

## Risks and Mitigations

**Risk: MonthYearDisplay format divergence.** The current `GetHybridWithCurrentMonthAsync` inlines `$"{now.Month:D2}/{now.Year}"` (month first). `MonthlyFinancialData.MonthYearDisplay` is documented as `$"{Month:D2}/{Year}"`. The `MapToDto` body must use `$"{month:D2}/{year}"` — month-first — to match both existing patterns. Do not accidentally write `$"{year}/{month:D2}"`.  
_Mitigation: The spec is explicit. Verify format string before committing._

**Risk: TotalStockValueChange type cast.** In `GetCachedFinancialOverview`, `cachedStockData?.TotalStockValueChange` is used as-is; in `GetFinancialOverviewRealTimeAsync`, the `CreateStockSummary` overload that accepts `List<MonthlyStockChange>` explicitly casts with `(decimal)sc.TotalStockValueChange`. Confirm `MonthlyStockChange.TotalStockValueChange` is already `decimal` (or that an implicit conversion applies) so `MapToDto` does not require an explicit cast. If the property is `double`, `float`, or another numeric type, an explicit cast is needed — but current call sites in `GetHybridWithCurrentMonthAsync` and `GetCachedFinancialOverview` use it without casting, implying it is `decimal`.  
_Mitigation: Check `MonthlyStockChange` definition before finalizing; adjust cast if needed._

**Risk: Behavioral regression in LINQ Select lambda.** The Select lambda in `GetFinancialOverviewRealTimeAsync` is a statement lambda (uses `return`), not an expression lambda — it performs a dictionary lookup before constructing the DTO. After extraction, the `stockChangeData` local variable must remain inside the lambda; only the `new MonthlyFinancialDataDto { ... }` construction is replaced by `MapToDto(...)`. Do not move the lookup logic into `MapToDto`.  
_Mitigation: The spec correctly places this as an in-lambda call with the resolved `stockChangeData` passed as argument._

No other risks identified. Build and format validation per project rules (`dotnet build`, `dotnet format`) are sufficient verification given the scope.

## Specification Amendments

None required. The spec is complete and unambiguous for this scope. One clarification recorded for the implementer:

- In FR-4, "Replace call-site in GetFinancialOverviewRealTimeAsync LINQ Select lambda" means replace only the `new MonthlyFinancialDataDto { ... }` block (lines 517–535). The surrounding lambda structure (variable binding for `stockChangeData`, `return` statement) stays in place.

## Prerequisites

None. No migrations, no environment changes, no dependency additions, no interface changes. The only prerequisite is that the file is not simultaneously modified by another in-flight branch.
