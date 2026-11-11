### task: write-manufacture-cost-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/ManufactureCostTests.cs`

- [ ] **Step 1: Create the test file** with namespace `Anela.Heblo.Tests.Features.Catalog`, usings `Anela.Heblo.Domain.Features.Catalog`, `FluentAssertions`, `Xunit`. Add private helper `Create(DateTime date)` returning `new ManufactureCost { Date = date, MaterialCostFromReceiptDocument = 100m, MaterialCostFromPurchasePrice = 60m, HandlingCost = 7m }`.

- [ ] **Step 2: Add tests** (use `new DateTime(y,m,d[,h,mi,s])`, never string parsing):
  - Theory `MaterialCost_BeforeCutoff_UsesPurchasePrice`: (2024,1,1), (2025,8,24) -> `MaterialCost` is 60m.
  - Theory `MaterialCost_AfterCutoff_UsesReceiptDocument`: (2025,8,26), (2026,1,1) -> 100m.
  - Fact `MaterialCost_ExactlyAtCutoffMidnight_UsesPurchasePrice`: 2025-08-25 00:00 -> 60m (strict `>`).
  - Fact `MaterialCost_SameDayAfterMidnight_UsesReceiptDocument`: 2025-08-25 12:00 -> 100m.
  - Fact `MaterialCost_LastSecondBeforeCutoffDay_UsesPurchasePrice`: 2025-08-24 23:59:59 -> 60m.
  - Fact `Total_BeforeCutoff_SumsPurchasePriceAndHandling`: 67m.
  - Fact `Total_AfterCutoff_SumsReceiptDocumentAndHandling`: 107m.
  - Fact `Total_DefaultInstance_IsZero`: `new ManufactureCost().Total` is 0m.

- [ ] **Step 3: Run and verify**
  - `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufactureCostTests"` -> all pass.
  - Mutation sanity check (do not commit): temporarily change `>` to `>=` in `ManufactureCost.cs`, confirm the midnight-boundary test fails, then revert with `git checkout`.
  - `dotnet build` and `dotnet format` (no unrelated diffs; only the new test file changed).

- [ ] **Step 4: Commit** `test(catalog): cover ManufactureCost date-based material cost switch` (refs #3958). No process doc update needed (no behaviour change).
