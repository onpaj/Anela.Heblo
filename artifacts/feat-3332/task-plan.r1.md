# Task Plan: Remove Explicit GC.Collect() from CatalogAnalyticsSourceAdapter

## Overview
Single-task plan: delete the `GC.Collect()` call on line 36 of `CatalogAnalyticsSourceAdapter.cs`, then verify the build and format checks pass. No interface changes, no new files, no test modifications required.

---

### task: remove-gc-collect

#### Goal
Delete the `GC.Collect();` call that fires after every 100-product batch inside `StreamProductsWithSalesAsync`. The streaming loop structure, batch size constant, and all other logic remain untouched.

#### Context
`CatalogAnalyticsSourceAdapter` implements `IAnalyticsProductSource` and streams `AnalyticsProduct` values to Analytics handlers in batches of 100. After yielding each batch it calls `GC.Collect()`, which forces a synchronous full garbage collection on every iteration — unnecessary and harmful to throughput. The fix is a one-line deletion; the `for` loop, `yield return`, and `cancellationToken.ThrowIfCancellationRequested()` lines are unaffected.

Relevant file state (before change):
```csharp
for (int i = 0; i < allProducts.Count; i += BatchSize)
{
    var batch = allProducts.Skip(i).Take(BatchSize);
    foreach (var product in batch)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return MapToAnalyticsProduct(product, fromDate, toDate);
    }
    GC.Collect();   // ← line 36 — DELETE THIS LINE
}
```

Nine existing unit tests in `CatalogAnalyticsSourceAdapterTests.cs` cover type mapping, margin fallback, sales filtering, purchase price selection, and null handling. None test GC behaviour; all pass without modification.

#### Files to create/modify
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs` — delete line 36 (`GC.Collect();`)

#### Implementation steps
1. Open `CatalogAnalyticsSourceAdapter.cs` and locate the `for` loop inside `StreamProductsWithSalesAsync` (lines 28–37).
2. Delete the line `GC.Collect();` (line 36). The closing brace of the `for` block on line 37 must remain. The result should be:
   ```csharp
   for (int i = 0; i < allProducts.Count; i += BatchSize)
   {
       var batch = allProducts.Skip(i).Take(BatchSize);
       foreach (var product in batch)
       {
           cancellationToken.ThrowIfCancellationRequested();
           yield return MapToAnalyticsProduct(product, fromDate, toDate);
       }
   }
   ```
3. Run `dotnet build` from the repository root (or the `backend/` directory) and confirm zero errors and zero new warnings.
4. Run `dotnet format --verify-no-changes` on the modified file and confirm no diff is produced.

#### Tests to write
No new tests are required. The deletion introduces no new behaviour — it only removes an explicit GC trigger. The nine existing tests in `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs` are sufficient coverage and must all continue to pass without modification.

#### Acceptance criteria
- `GC.Collect();` no longer appears anywhere in `CatalogAnalyticsSourceAdapter.cs`.
- The `for` loop, `foreach` block, `cancellationToken.ThrowIfCancellationRequested()`, and `yield return` lines are byte-for-byte identical to the pre-change state.
- `dotnet build` exits with code 0, no errors, no new warnings.
- `dotnet format --verify-no-changes` exits with code 0 (no formatting diff).
- All 9 tests in `CatalogAnalyticsSourceAdapterTests.cs` pass (`dotnet test` green).
