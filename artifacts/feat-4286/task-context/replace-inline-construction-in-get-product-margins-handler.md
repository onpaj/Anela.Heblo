### task: replace-inline-construction-in-get-product-margins-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs:209-270`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` (existing — run, do not modify unless it fails)

**Depends on:** `add-marginleveldto-factory` (needs `MarginLevelDto.FromDomain` to exist).

- [ ] **Step 1: Run the existing test suite first to capture the current-passing baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"`
Expected: PASS (all existing tests green before this task's edit).

- [ ] **Step 2: Replace the M0-M3 averages block**

In `GetProductMarginsHandler.cs`, inside `MapToMarginDto`, replace:

```csharp
                // Use pre-calculated averages from margin history
                M0 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M0.Percentage,
                    Amount = marginHistory.Averages.M0.Amount,
                    CostLevel = marginHistory.Averages.M0.CostLevel,
                    CostTotal = marginHistory.Averages.M0.CostTotal
                },
                M1 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M1.Percentage,
                    Amount = marginHistory.Averages.M1.Amount,
                    CostLevel = marginHistory.Averages.M1.CostLevel,
                    CostTotal = marginHistory.Averages.M1.CostTotal
                },
                M2 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M2.Percentage,
                    Amount = marginHistory.Averages.M2.Amount,
                    CostLevel = marginHistory.Averages.M2.CostLevel,
                    CostTotal = marginHistory.Averages.M2.CostTotal
                },
                M3 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M3.Percentage,
                    Amount = marginHistory.Averages.M3.Amount,
                    CostLevel = marginHistory.Averages.M3.CostLevel,
                    CostTotal = marginHistory.Averages.M3.CostTotal
                },
```

with:

```csharp
                // Use pre-calculated averages from margin history
                M0 = MarginLevelDto.FromDomain(marginHistory.Averages.M0),
                M1 = MarginLevelDto.FromDomain(marginHistory.Averages.M1),
                M2 = MarginLevelDto.FromDomain(marginHistory.Averages.M2),
                M3 = MarginLevelDto.FromDomain(marginHistory.Averages.M3),
```

- [ ] **Step 3: Replace the MonthlyHistory projection block**

In the same method, replace:

```csharp
                // Monthly history for charts (filtered to last 13 months)
                MonthlyHistory = filteredMonthlyData.Select(m => new MonthlyMarginDto
                {
                    Month = m.Key,
                    M0 = new MarginLevelDto
                    {
                        Percentage = m.Value.M0.Percentage,
                        Amount = m.Value.M0.Amount,
                        CostLevel = m.Value.M0.CostLevel,
                        CostTotal = m.Value.M0.CostTotal
                    },
                    M1 = new MarginLevelDto
                    {
                        Percentage = m.Value.M1.Percentage,
                        Amount = m.Value.M1.Amount,
                        CostLevel = m.Value.M1.CostLevel,
                        CostTotal = m.Value.M1.CostTotal
                    },
                    M2 = new MarginLevelDto
                    {
                        Percentage = m.Value.M2.Percentage,
                        Amount = m.Value.M2.Amount,
                        CostLevel = m.Value.M2.CostLevel,
                        CostTotal = m.Value.M2.CostTotal
                    },
                    M3 = new MarginLevelDto
                    {
                        Percentage = m.Value.M3.Percentage,
                        Amount = m.Value.M3.Amount,
                        CostLevel = m.Value.M3.CostLevel,
                        CostTotal = m.Value.M3.CostTotal
                    }
                }).ToList()
```

with:

```csharp
                // Monthly history for charts (filtered to last 13 months)
                MonthlyHistory = filteredMonthlyData.Select(m => new MonthlyMarginDto
                {
                    Month = m.Key,
                    M0 = MarginLevelDto.FromDomain(m.Value.M0),
                    M1 = MarginLevelDto.FromDomain(m.Value.M1),
                    M2 = MarginLevelDto.FromDomain(m.Value.M2),
                    M3 = MarginLevelDto.FromDomain(m.Value.M3)
                }).ToList()
```

- [ ] **Step 4: Confirm no other `new MarginLevelDto` remains in this file**

Run: `grep -n "new MarginLevelDto" backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`
Expected: no output (0 matches).

- [ ] **Step 5: Run tests to verify they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"`
Expected: PASS — identical result set to Step 1's baseline.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs
git commit -m "refactor(catalog): use MarginLevelDto.FromDomain in GetProductMarginsHandler"
```
