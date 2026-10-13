### task: full-suite-validation

**Files:** none created/modified — validation only.

- [ ] **Step 1: Full backend build**

Run: `dotnet build Anela.Heblo.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or the same pre-existing warning count as `main` — this refactor introduces no new warnings).

- [ ] **Step 2: Format check**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: no formatting violations. If violations are reported, run `dotnet format Anela.Heblo.sln`, review the diff is whitespace-only, then re-run Step 1.

- [ ] **Step 3: Full Purchase-module test run**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"`
Expected: PASS — includes `StockAnalysisCalculatorTests` (new), `GetPurchaseStockAnalysisHandlerTests` (585 lines, unmodified assertions), `GetPurchaseStockAnalysisHandlerDiacriticsTests` (104 lines, unmodified assertions).

- [ ] **Step 4: Full test project run (regression check for anything referencing the changed types elsewhere)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS, same pass count as on `main` plus the new `StockAnalysisCalculatorTests` cases.

- [ ] **Step 5: Confirm no unrelated files changed**

Run: `git diff --stat main...HEAD`
Expected: only the five files touched across Tasks 1–4 (`IStockAnalysisCalculator.cs`, `StockAnalysisCalculator.cs`, `GetPurchaseStockAnalysisHandler.cs`, `GetPurchaseStockAnalysisHandlerTests.cs`, `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`) plus the new `StockAnalysisCalculatorTests.cs`, plus this plan's own `artifacts/feat-4200/` files. No `PurchaseModule.cs` change (DI registration for `IStockAnalysisCalculator` already existed), no OpenAPI/frontend client regeneration (no public contract changed).

- [ ] **Step 6: Final commit (if Steps 2's format run produced changes not already committed)**

```bash
git add -A
git commit -m "chore(purchase): dotnet format after stock-analysis refactor" --allow-empty
```
