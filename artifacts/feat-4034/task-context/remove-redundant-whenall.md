### task: remove-redundant-whenall

**File to change:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`

**What to do:** In the private method `CalculateMonthlyStockChangeAsync`, delete the following line in its entirety (currently line 141):

```csharp
await Task.WhenAll(startStockTasks.Concat(endStockTasks));
```

**Exact surrounding context** (so the line to delete is unambiguous — this is the full method body before the change):

```csharp
    private async Task<MonthlyStockChange> CalculateMonthlyStockChangeAsync(
        DateTime monthStart,
        Dictionary<string, decimal> priceDict,
        CancellationToken cancellationToken)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // Get stock values at start and end of month for each warehouse
        var startStockTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, monthStart, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthStart, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, monthStart, priceDict, cancellationToken)
        };

        var endStockTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, monthEnd, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthEnd, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, monthEnd, priceDict, cancellationToken)
        };

        await Task.WhenAll(startStockTasks.Concat(endStockTasks));   // <-- DELETE THIS LINE (and the blank line either directly above or below it, so exactly one blank line remains between the endStockTasks initializer and the next statement, matching the surrounding blank-line style)

        var startValues = await Task.WhenAll(startStockTasks);
        var endValues = await Task.WhenAll(endStockTasks);

        // Calculate changes (end - start for each warehouse)
        var materialsChange = endValues[0] - startValues[0];
        var semiProductsChange = endValues[1] - startValues[1];
        var productsChange = endValues[2] - startValues[2];

        return new MonthlyStockChange
        {
            Year = monthStart.Year,
            Month = monthStart.Month,
            StockChanges = new StockChangeByType
            {
                Materials = materialsChange,
                SemiProducts = semiProductsChange,
                Products = productsChange
            }
        };
    }
```

After the change, the method body must read exactly as above but with the marked line removed, i.e.:

```csharp
        var endStockTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, monthEnd, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthEnd, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, monthEnd, priceDict, cancellationToken)
        };

        var startValues = await Task.WhenAll(startStockTasks);
        var endValues = await Task.WhenAll(endStockTasks);
```

Do not change anything else in the file: not the two remaining `Task.WhenAll` awaits, not the method signature, not any other method (in particular leave `GetStockValueChangeForPeriodAsync` and `GetWarehouseStockValueAsync` untouched), and not any `using` directives — `dotnet format`/the compiler will flag if any `using` becomes unused, but none is expected to.

**Why this is safe (no behavior change):** All six tasks in `startStockTasks` and `endStockTasks` are already created — and therefore already started — before any `await` is reached (task creation, not awaiting, is what starts execution for these `Task<decimal>`-returning calls). The deleted line awaited the combined set of tasks but discarded the result; the two awaits that remain (`await Task.WhenAll(startStockTasks)` and `await Task.WhenAll(endStockTasks)`) already await the same underlying tasks and are the only ones whose results are used, to populate `startValues` and `endValues`. Removing the discarded-result await is a pure no-op for behavior, correctness, and performance. It also makes this method structurally match the already-correct sibling method `GetStockValueChangeForPeriodAsync` in the same file, which implements the identical start/end concurrent-fetch pattern without any redundant combined await.

**Verification steps:**
1. Run the existing test suite that covers this code path: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/StockValueServiceTests.cs`. It exercises `GetStockValueChangesAsync`, which calls `CalculateMonthlyStockChangeAsync` once per month, with mocked `IErpStockClient`/`IProductPriceErpClient`, and asserts the computed monthly stock-change values. From the `backend` directory (or the solution root), run:
   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~StockValueServiceTests
   ```
   (adjust the test project path if it differs from this — locate it via the file path above if needed). All tests in this file must pass unchanged; no new or modified tests are needed for this change since the deleted line was unreachable dead code with no observable behavior.
2. Build the backend: `dotnet build` (run from the solution/repo root, or against the `Anela.Heblo.Application` project / full solution) — must succeed with no new errors or warnings.
3. Format check: `dotnet format` (per this repo's CLAUDE.md validation requirements for backend changes) — must complete cleanly with no unexpected diffs beyond the intended deletion.
4. Confirm via a final read of the file that line 141 (`await Task.WhenAll(startStockTasks.Concat(endStockTasks));`) is gone and that no other line in `FinancialOverviewStockValueAdapter.cs` was modified (e.g. `git diff backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs` should show exactly a one-line removal, plus at most a blank-line adjustment immediately around it).

**Definition of done:** The line is deleted, `dotnet build` succeeds, `dotnet format` reports no issues, `StockValueServiceTests.cs` passes in full, and `git diff` for the changed file shows only this single-line (plus adjacent blank line, if applicable) removal.
