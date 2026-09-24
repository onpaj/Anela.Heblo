### task: replace-inline-construction-in-get-catalog-detail-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs:211-245`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` (existing — run, do not modify unless it fails)
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` (existing — run, do not modify unless it fails)

**Depends on:** `add-marginleveldto-factory` (needs `MarginLevelDto.FromDomain` to exist). Independent of `replace-inline-construction-in-get-product-margins-handler` (different file) — can run in parallel with it once the factory task is done.

- [ ] **Step 1: Run the existing test suites first to capture the current-passing baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetCatalogDetailHandlerTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests"`
Expected: PASS (all existing tests green before this task's edit).

- [ ] **Step 2: Replace the M0-M3 block in `GetMarginHistoryFromMargins`**

In `GetCatalogDetailHandler.cs`, inside `GetMarginHistoryFromMargins`, replace:

```csharp
                // M0 - Material + Manufacturing costs
                M0 = new MarginLevelDto
                {
                    Percentage = m.Value.M0.Percentage,
                    Amount = m.Value.M0.Amount,
                    CostLevel = m.Value.M0.CostLevel,
                    CostTotal = m.Value.M0.CostTotal
                },

                // M1 - M0 + Manufacturing costs (if different)
                M1 = new MarginLevelDto
                {
                    Percentage = m.Value.M1.Percentage,
                    Amount = m.Value.M1.Amount,
                    CostLevel = m.Value.M1.CostLevel,
                    CostTotal = m.Value.M1.CostTotal
                },

                // M2 - M1 + Sales costs
                M2 = new MarginLevelDto
                {
                    Percentage = m.Value.M2.Percentage,
                    Amount = m.Value.M2.Amount,
                    CostLevel = m.Value.M2.CostLevel,
                    CostTotal = m.Value.M2.CostTotal
                },

                // M3 - M2 + Overhead (final margin level)
                M3 = new MarginLevelDto
                {
                    Percentage = m.Value.M3.Percentage,
                    Amount = m.Value.M3.Amount,
                    CostLevel = m.Value.M3.CostLevel,
                    CostTotal = m.Value.M3.CostTotal
                }
```

with:

```csharp
                // M0 - Material + Manufacturing costs
                M0 = MarginLevelDto.FromDomain(m.Value.M0),

                // M1 - M0 + Manufacturing costs (if different)
                M1 = MarginLevelDto.FromDomain(m.Value.M1),

                // M2 - M1 + Sales costs
                M2 = MarginLevelDto.FromDomain(m.Value.M2),

                // M3 - M2 + Overhead (final margin level)
                M3 = MarginLevelDto.FromDomain(m.Value.M3)
```

- [ ] **Step 3: Confirm no other `new MarginLevelDto` remains in this file**

Run: `grep -n "new MarginLevelDto" backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`
Expected: no output (0 matches).

- [ ] **Step 4: Run tests to verify they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetCatalogDetailHandlerTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests"`
Expected: PASS — identical result set to Step 1's baseline.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs
git commit -m "refactor(catalog): use MarginLevelDto.FromDomain in GetCatalogDetailHandler"
```
