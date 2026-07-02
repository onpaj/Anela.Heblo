# Task Plan: Extract MapToDto Factory Method

## Overview

Add a single `private static MonthlyFinancialDataDto MapToDto(...)` method to `FinancialAnalysisService` and replace all three inline `MonthlyFinancialDataDto` construction blocks with calls to it. This is a pure structural refactoring — no behavioral change, one file touched.

The pattern already exists in the class: `CreateStockSummary` has two private static overloads. `MapToDto` follows the same convention. All types involved are confirmed:

- `MonthlyFinancialData.TotalStockValueChange` is `decimal` — no cast needed in `MapToDto`.
- `MonthlyFinancialData.MonthYearDisplay` computes as `$"{Month:D2}/{Year}"` — `MapToDto` uses the same format string, so the cached path is safe to switch.
- The LINQ Select lambda in `GetFinancialOverviewRealTimeAsync` is a statement lambda; the `stockChangeData` lookup local variable stays inside the lambda and only the DTO construction is replaced.

---

### task: extract-map-to-dto

**Goal:** Extract `MapToDto` factory method and replace all three call sites.

**Context:**

`FinancialAnalysisService` (single file, 588 lines) has three near-identical `MonthlyFinancialDataDto` construction blocks in three different methods. All apply identical conditional logic for `StockChanges`, `TotalStockValueChange`, and `TotalBalance`. Divergence between the three blocks is a latent bug risk.

Key type confirmations (from reading source files):
- `MonthlyStockChange.TotalStockValueChange` is `decimal` (computed property) — no cast needed.
- `MonthlyFinancialData.MonthYearDisplay` formats as `$"{Month:D2}/{Year}"` — identical to what `MapToDto` will produce.
- `MonthlyFinancialDataDto` is a plain class (not a record), consistent with project rules.

**Files to modify:**
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`

**Implementation steps:**

1. **Add `MapToDto` method (FR-1, FR-5).** Insert the following private static method immediately after the closing brace of the second `CreateStockSummary` overload (currently ending at line 586, before `}`of the class):

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

   Format note: use `$"{month:D2}/{year}"` (month first). Do not write `$"{year}/{month:D2}"`.

2. **Replace FR-2 call site in `GetHybridWithCurrentMonthAsync` (lines 300–321).** Remove the `var financialBalance = income - expenses;` local variable (line 300) and the entire `var currentMonthDto = new MonthlyFinancialDataDto { ... }` block (lines 303–321). Replace both with:

   ```csharp
   var currentMonthDto = MapToDto(now.Year, now.Month, income, expenses, stockChange, includeStockData);
   ```

   The `financialBalance` local is no longer needed at this call site — `MapToDto` computes it internally. Verify no other usage of `financialBalance` exists between line 300 and line 329 before removing it.

3. **Replace FR-3 call site in `GetCachedFinancialOverview` (lines 381–399).** Remove the entire `monthlyData.Add(new MonthlyFinancialDataDto { ... });` block and replace with:

   ```csharp
   monthlyData.Add(MapToDto(
       cachedFinancialData.Year,
       cachedFinancialData.Month,
       cachedFinancialData.Income,
       cachedFinancialData.Expenses,
       cachedStockData,
       includeStockData));
   ```

   The `MonthYearDisplay` and `FinancialBalance` fields were previously read from `cachedFinancialData`'s computed properties, which produce the same values as `MapToDto` will generate — no behavioral change.

4. **Replace FR-4 call site in `GetFinancialOverviewRealTimeAsync` LINQ Select lambda (lines 513–536).** The current statement lambda performs a dictionary lookup then constructs the DTO. Keep the lookup and `return`; replace only the `new MonthlyFinancialDataDto { ... }` block:

   ```csharp
   .Select(d =>
   {
       var stockChangeData = stockChangesLookup.TryGetValue(new { d.Year, d.Month }, out var stockChange)
           ? stockChange
           : null;
       return MapToDto(d.Year, d.Month, d.Income, d.Expenses, stockChangeData, includeStockData);
   }).ToList(),
   ```

   Do not move the `stockChangesLookup.TryGetValue(...)` call into `MapToDto`. It stays in the lambda.

5. **Verify build and format.** Run:
   - `dotnet build` from the solution root — must complete with no errors.
   - `dotnet format` — must produce no diff (i.e., the file is already correctly formatted after the edit, or format it first and verify no net change).

**Acceptance criteria:**

- [ ] `MapToDto` private static method exists in `FinancialAnalysisService` with the exact signature `MapToDto(int year, int month, decimal income, decimal expenses, MonthlyStockChange? stockChange, bool includeStockData)`.
- [ ] Method body: `MonthYearDisplay` uses `$"{month:D2}/{year}"` (month-first format).
- [ ] Method body: `TotalStockValueChange` assigned without explicit cast (property is already `decimal`).
- [ ] `MapToDto` is placed after the two `CreateStockSummary` overloads, before the closing `}` of the class.
- [ ] The `var financialBalance = income - expenses;` local variable in `GetHybridWithCurrentMonthAsync` (previously line 300) is removed — it is fully subsumed by `MapToDto`.
- [ ] All three inline `MonthlyFinancialDataDto` construction blocks replaced with `MapToDto(...)` calls.
- [ ] The `stockChangesLookup.TryGetValue(...)` lookup in the LINQ Select lambda remains inside the lambda body (not moved into `MapToDto`).
- [ ] `dotnet build` passes with no errors or new warnings.
- [ ] `dotnet format` produces no diff after the change.
- [ ] No other files are modified.
- [ ] No behavioral change: output of all three code paths is identical to pre-refactoring.
