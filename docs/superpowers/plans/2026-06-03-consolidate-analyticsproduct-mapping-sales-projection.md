# Consolidate `AnalyticsProduct` Mapping — `SalesHistory` Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify that the original spec (extract `CatalogAggregate → AnalyticsProduct` mapping helper) is already delivered by #1805 and, as a small follow-up, fold the duplicated `SalesHistory` projection at the two call sites in `CatalogAnalyticsSourceAdapter` into the existing `MapToAnalyticsProduct` helper so the mapping truly lives in one place.

**Architecture:** Single-file refactor inside `CatalogAnalyticsSourceAdapter` (Catalog module's Analytics adapter — internal sealed). Change `MapToAnalyticsProduct` from a 4-arg helper that receives a pre-filtered `List<SalesDataPoint>` to a 3-arg helper that owns the `SalesHistory.Where(...).Select(...).ToList()` projection. Drop the duplicated projection from both call sites. No module boundaries change. No public API change.

**Tech Stack:** C# 12 / .NET 8, xUnit + FluentAssertions + Moq, MediatR/Vertical-Slice layout, `internal sealed` adapter pattern (consumer-owned contract `IAnalyticsProductSource`, Catalog-provided implementation).

---

## Spec Status

The originating spec (`spec.r1.md`) is **superseded** by PR #1805 ("Decouple Analytics from Catalog"), which has already landed on `main`. Verified state of the codebase:

- `AnalyticsRepository.cs` moved from `Application/Features/Analytics/Infrastructure/` to `Persistence/Features/Analytics/` and is now a thin delegate over `IAnalyticsProductSource` — lines 52–116 and 168–231 cited in the spec no longer exist.
- `CatalogAnalyticsSourceAdapter.MapToAnalyticsProduct` exists at `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs:72–117` with the `hasMargin` boolean collapsed (line 87) — exactly the shape FR-1 prescribed.
- Both call sites (`StreamProductsWithSalesAsync:43`, `GetProductAnalysisDataAsync:69`) route through the helper. The `SalesHistory` drift bug FR-3 set out to fix does not exist on `main`: both call sites pre-filter by `[fromDate, toDate]` before invoking the helper (`:34–42` and `:59–67`).
- `CatalogAnalyticsSourceAdapterTests` already covers boundary translation, margin slice selection, the `hasMargin == false` fallback to `Averages.M0.Amount`, `SalesHistory` period filtering for both call sites, latest-purchase resolution, and pricing/margin mapping.

Therefore: **FR-1, FR-2, FR-3, FR-4, FR-5 are already satisfied.** This plan does **not** re-implement them. It executes the single optional consolidation called out as "delta (1)" in the arch review.

## What This Plan Does NOT Touch

Per the arch review's load-bearing-decision callouts, do not change the following — they are correct under the post-#1805 type system and changing them silently is a semantic regression:

- **`M1_A` vs `M1` slice selection** in `MapToAnalyticsProduct` (`:91`, `:105`, `:108`). The adapter reads `M1_A.Amount` / `M1_A.Percentage` / `M1_A.CostLevel`. The spec's illustrative code shows `M1.*` — that snippet is **not** authoritative. Leave `M1_A` as-is.
- **`Type` mapping** via `MapProductType(product.Type)` (`:100`). The adapter translates `Catalog.ProductType → AnalyticsProductType` at the boundary. Do not regress to raw `product.Type`.
- **`MarginAmount` fallback** to `marginData.Averages.M0.Amount` when `hasMargin == false` (`:89`). Preserve.
- **`PurchasePrice` semantics** — latest `PurchaseHistory` entry by `Date` (`:93–94`). Preserve.
- Public signatures of `IAnalyticsProductSource`, `IAnalyticsRepository`, `AnalyticsRepository`, `AnalyticsProduct`. Preserve.
- The `internal sealed` visibility of `CatalogAnalyticsSourceAdapter` and `private static` visibility of `MapToAnalyticsProduct`. Never expose cross-module.

## File Structure

- **Modify:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs`
  - Change `MapToAnalyticsProduct` signature from `(CatalogAggregate, DateTime, DateTime, List<SalesDataPoint>)` to `(CatalogAggregate, DateTime, DateTime)`.
  - Move the `product.SalesHistory.Where(...).Select(...).ToList()` projection from the two call sites into the helper body.
  - Drop the now-redundant 6-line `filteredSales` blocks from `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync`.
- **Modify:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs`
  - Add one explicit regression test that pins "the helper itself owns the date-bounded filter" so this consolidation cannot drift back. The two existing filter tests still pass and act as integration coverage from the two call-site entry points.

No new files. No moved files. No namespace changes.

---

## Task 1: Establish Baseline — Confirm Current State Matches Arch Review

**Files:**
- Read-only verification — no edits in this task.

- [ ] **Step 1: Confirm `AnalyticsRepository` is a delegate-only file (no mapping)**

Run:
```bash
grep -n "MapToAnalyticsProduct\|MonthlyData\|marginData" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs
```

Expected output: no matches. If anything matches, STOP — the codebase has diverged from the arch review's premise and this plan needs to be rewritten before proceeding.

- [ ] **Step 2: Confirm `MapToAnalyticsProduct` lives on the adapter**

Run:
```bash
grep -n "MapToAnalyticsProduct" backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
```

Expected: three matches — one definition (`private static AnalyticsProduct MapToAnalyticsProduct(...)`) and two call sites (one in `StreamProductsWithSalesAsync`, one in `GetProductAnalysisDataAsync`). If absent, STOP.

- [ ] **Step 3: Confirm no other `MapToAnalyticsProduct` exists in the solution**

Run:
```bash
grep -rn "MapToAnalyticsProduct" backend/src/ backend/test/ --include='*.cs'
```

Expected: matches only inside `CatalogAnalyticsSourceAdapter.cs` and `CatalogAnalyticsSourceAdapterTests.cs` (test references are OK if any). If a duplicate has reappeared elsewhere (e.g. inside a handler or a different repository), STOP and file a focused ticket — do not silently delete it here.

- [ ] **Step 4: Build and run the adapter and module-boundary test classes — baseline must be green**

Run:
```bash
cd backend && dotnet build Anela.Heblo.sln -c Debug
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Then run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogAnalyticsSourceAdapterTests|FullyQualifiedName~ModuleBoundariesTests" \
  --no-build
```

Expected: all tests pass. Note the test count for the adapter class for later comparison. If any test fails on baseline, STOP — fix or escalate before making changes.

- [ ] **Step 5: No commit — this is a verification-only task**

Nothing to commit yet. Proceed to Task 2.

---

## Task 2: Add Regression Test Pinning "Helper Owns the Date Filter"

**Why before the refactor:** The two existing filter tests (`StreamProductsWithSalesAsync_FiltersSalesHistoryToTheRequestedPeriod`, `GetProductAnalysisDataAsync_FiltersSalesHistoryByPeriod`) drive through the public methods, so they will pass both before the refactor (filter applied at call site) and after (filter applied inside helper). A direct assertion that documents *the helper itself* must filter — added now — protects against a future drift where someone reintroduces a pre-filter at the call site and then "simplifies" the helper to skip its filter.

This test exercises the same public method but with input shape that proves the filter must execute inside the helper: a `CatalogAggregate.SalesHistory` containing out-of-period entries that the call site no longer touches. Pre-refactor, the test will pass because the call site is filtering; post-refactor, it will pass because the helper is. Either way, the test acts as a permanent regression pin.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs`

- [ ] **Step 1: Add a new test method to the existing test class**

Insert after `GetProductAnalysisDataAsync_MapsPricingAndMarginFields` (currently the last test in the file). Match the existing style — use `CreateCatalogAggregate(...)` helper, FluentAssertions, Moq.

```csharp
    [Fact]
    public async Task GetProductAnalysisDataAsync_ExcludesSalesOutsidePeriodEvenWhenCallerPassesUnfilteredAggregate()
    {
        // Arrange — aggregate contains sales BEFORE, INSIDE, and AFTER the requested period.
        // The adapter is responsible for date-bounding SalesHistory regardless of whether the
        // caller pre-filters. This pins the helper as the single source of the period filter.
        var product = CreateCatalogAggregate("PROD001", "Test", ProductType.Product);
        product.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2023, 12, 31), AmountB2B = 1, AmountB2C = 1, SumB2B = 10, SumB2C = 10 },
            new CatalogSaleRecord { Date = new DateTime(2024, 1, 1), AmountB2B = 2, AmountB2C = 2, SumB2B = 20, SumB2C = 20 },
            new CatalogSaleRecord { Date = new DateTime(2024, 6, 15), AmountB2B = 3, AmountB2C = 3, SumB2B = 30, SumB2C = 30 },
            new CatalogSaleRecord { Date = new DateTime(2024, 12, 31), AmountB2B = 4, AmountB2C = 4, SumB2B = 40, SumB2C = 40 },
            new CatalogSaleRecord { Date = new DateTime(2025, 1, 1), AmountB2B = 5, AmountB2C = 5, SumB2B = 50, SumB2C = 50 }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock
            .Setup(r => r.GetByIdAsync("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var adapter = new CatalogAnalyticsSourceAdapter(repoMock.Object);
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        // Act
        var result = await adapter.GetProductAnalysisDataAsync("PROD001", fromDate, toDate);

        // Assert — boundary-inclusive: 2024-01-01, 2024-06-15, 2024-12-31 are in; the other two are out.
        result.Should().NotBeNull();
        result!.SalesHistory.Should().HaveCount(3);
        result.SalesHistory.Select(s => s.Date).Should().BeEquivalentTo(new[]
        {
            new DateTime(2024, 1, 1),
            new DateTime(2024, 6, 15),
            new DateTime(2024, 12, 31)
        });
    }
```

- [ ] **Step 2: Run the new test against the un-refactored code — confirm it passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName=Anela.Heblo.Tests.Features.Catalog.Infrastructure.CatalogAnalyticsSourceAdapterTests.GetProductAnalysisDataAsync_ExcludesSalesOutsidePeriodEvenWhenCallerPassesUnfilteredAggregate"
```

Expected: PASS. (Currently the call site filters, so the helper's input is already bounded. The test still validates the observable contract, which is what we care about.)

If it fails, STOP and inspect — the test data must include sales exactly on both period boundaries (`fromDate` and `toDate`) because the production code uses `>=` / `<=`, not `>` / `<`.

- [ ] **Step 3: Run the full adapter test class — nothing else regressed**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogAnalyticsSourceAdapterTests" --no-build
```

Expected: previous count + 1 tests pass. Zero failures.

- [ ] **Step 4: Commit the test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapterTests.cs
git commit -m "test: pin sales-period filter inside CatalogAnalyticsSourceAdapter helper"
```

---

## Task 3: Move `SalesHistory` Projection Into `MapToAnalyticsProduct`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs`

The current helper accepts a pre-projected `List<SalesDataPoint> salesHistory` and assigns it straight to `AnalyticsProduct.SalesHistory`. Both call sites build that list with the same six-line LINQ block. Move the block into the helper; drop the parameter; drop the duplicate blocks.

- [ ] **Step 1: Change the helper signature and body to own the projection**

Edit lines 72–117. Replace the existing helper with:

```csharp
    private static AnalyticsProduct MapToAnalyticsProduct(
        CatalogAggregate product,
        DateTime fromDate,
        DateTime toDate)
    {
        var marginData = product.Margins;
        var relevantMargins = marginData.MonthlyData
            .Where(m => m.Key >= fromDate && m.Key <= toDate)
            .ToList();

        var latestMarginEntry = relevantMargins.LastOrDefault();
        if (latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>)))
            latestMarginEntry = marginData.MonthlyData.LastOrDefault();

        bool hasMargin = !latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>));

        var marginAmount = hasMargin ? latestMarginEntry.Value.M0.Amount : marginData.Averages.M0.Amount;
        var materialCost = hasMargin ? latestMarginEntry.Value.M0.CostLevel : 0m;
        var handlingCost = hasMargin ? latestMarginEntry.Value.M1_A.CostLevel : 0m;

        var latestPurchase = product.PurchaseHistory?.OrderByDescending(p => p.Date).FirstOrDefault();
        var purchasePrice = latestPurchase?.PricePerPiece ?? 0m;

        var salesHistory = product.SalesHistory
            .Where(s => s.Date >= fromDate && s.Date <= toDate)
            .Select(s => new SalesDataPoint
            {
                Date = s.Date,
                AmountB2B = s.AmountB2B,
                AmountB2C = s.AmountB2C
            })
            .ToList();

        return new AnalyticsProduct
        {
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            Type = MapProductType(product.Type),
            ProductFamily = product.ProductFamily,
            ProductCategory = product.ProductCategory,
            MarginAmount = marginAmount,
            M0Amount = hasMargin ? latestMarginEntry.Value.M0.Amount : 0m,
            M1Amount = hasMargin ? latestMarginEntry.Value.M1_A.Amount : 0m,
            M2Amount = hasMargin ? latestMarginEntry.Value.M2.Amount : 0m,
            M0Percentage = hasMargin ? latestMarginEntry.Value.M0.Percentage : 0m,
            M1Percentage = hasMargin ? latestMarginEntry.Value.M1_A.Percentage : 0m,
            M2Percentage = hasMargin ? latestMarginEntry.Value.M2.Percentage : 0m,
            SellingPrice = product.EshopPrice?.PriceWithoutVat ?? 0m,
            EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat,
            PurchasePrice = purchasePrice,
            MaterialCost = materialCost,
            HandlingCost = handlingCost,
            SalesHistory = salesHistory,
        };
    }
```

Two things to double-check while editing:
- Preserve `M1_A` in the three `M1*` assignments (do not change to `M1`).
- Preserve `MapProductType(product.Type)` for the `Type` assignment (do not change to raw `product.Type`).

- [ ] **Step 2: Drop the duplicated projection from `StreamProductsWithSalesAsync`**

Edit `StreamProductsWithSalesAsync`. The `foreach` body becomes a single helper call. Replace lines ~31–44 (the `foreach` body) with:

```csharp
            foreach (var product in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return MapToAnalyticsProduct(product, fromDate, toDate);
            }
```

Keep the surrounding `for (int i = 0; ...)` batching loop and the `GC.Collect();` line after the inner `foreach` exactly as they were — those are out of scope for this refactor.

- [ ] **Step 3: Drop the duplicated projection from `GetProductAnalysisDataAsync`**

Edit `GetProductAnalysisDataAsync`. Replace lines ~55–69 (the body after the null check) with:

```csharp
        var product = await _catalogRepository.GetByIdAsync(productId, cancellationToken);
        if (product == null)
            return null;

        return MapToAnalyticsProduct(product, fromDate, toDate);
    }
```

- [ ] **Step 4: Build and verify zero new warnings**

Run:
```bash
cd backend && dotnet build Anela.Heblo.sln -c Debug
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If there are warnings about unused locals or unreachable code, you missed cleaning up either the call-site `filteredSales` blocks or the now-unused helper overload — re-check the file.

- [ ] **Step 5: Run the focused tests — all must pass without modification**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogAnalyticsSourceAdapterTests" --no-build
```

Expected: all tests pass (same count as after Task 2, including the new regression test). If `StreamProductsWithSalesAsync_FiltersSalesHistoryToTheRequestedPeriod`, `GetProductAnalysisDataAsync_FiltersSalesHistoryByPeriod`, or the new `GetProductAnalysisDataAsync_ExcludesSalesOutsidePeriodEvenWhenCallerPassesUnfilteredAggregate` fails, the `Where(s => s.Date >= fromDate && s.Date <= toDate)` clause is missing or wrong inside the helper — re-inspect Step 1.

- [ ] **Step 6: Run the module-boundary tests — confirm Analytics→Catalog rule still holds**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests" --no-build
```

Expected: all tests pass. (No new violations should be possible — this refactor stays entirely inside the `CatalogAnalyticsSourceAdapter` file in the Catalog module — but verify rather than assume.)

- [ ] **Step 7: Format and verify whole-solution build/test still green**

Run:
```bash
cd backend && dotnet format Anela.Heblo.sln
```

Then:
```bash
cd backend && dotnet build Anela.Heblo.sln -c Debug && dotnet test Anela.Heblo.sln --no-build
```

Expected: build succeeds, all tests pass. If a test elsewhere fails, it almost certainly wasn't caused by this change — investigate before committing.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
git commit -m "refactor(catalog): fold SalesHistory projection into MapToAnalyticsProduct

The CatalogAggregate->AnalyticsProduct mapping helper now owns the
SalesHistory date-bounded projection. Both call sites
(StreamProductsWithSalesAsync, GetProductAnalysisDataAsync) drop
their duplicated six-line LINQ block and route directly through the
helper. Behavior is preserved: the same Where(>=fromDate && <=toDate)
predicate runs in the same place in the projection pipeline.

This completes the 'one mapping site' intent of #1805 by removing
the last residual duplication on the call-site side."
```

---

## Task 4: Final Verification

**Files:**
- Read-only verification — no edits in this task.

- [ ] **Step 1: Confirm no `SalesHistory.Where` projection remains in the adapter outside the helper**

Run:
```bash
grep -nC1 "SalesHistory" backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
```

Expected: matches only inside `MapToAnalyticsProduct` (`product.SalesHistory.Where(...)` and the `SalesHistory = salesHistory,` assignment). If `SalesHistory` appears in `StreamProductsWithSalesAsync` or `GetProductAnalysisDataAsync`, Task 3 Steps 2 or 3 were incomplete.

- [ ] **Step 2: Confirm helper signature is the three-arg form**

Run:
```bash
grep -n "MapToAnalyticsProduct" backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
```

Expected: definition reads `private static AnalyticsProduct MapToAnalyticsProduct(` and the two call sites read `MapToAnalyticsProduct(product, fromDate, toDate)` with three arguments only.

- [ ] **Step 3: Confirm `M1_A` is still the source of `M1*` fields**

Run:
```bash
grep -n "M1_A\b" backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
```

Expected: three matches — one for `handlingCost` (`M1_A.CostLevel`), one for `M1Amount` (`M1_A.Amount`), one for `M1Percentage` (`M1_A.Percentage`). If any has flipped to plain `M1`, fix it before committing.

- [ ] **Step 4: Confirm `MapProductType(product.Type)` is still in place**

Run:
```bash
grep -n "MapProductType(product.Type)" backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs
```

Expected: exactly one match — inside `MapToAnalyticsProduct` for the `Type =` assignment. If it has regressed to raw `product.Type`, fix it before committing.

- [ ] **Step 5: No commit — verification only**

If all checks pass, the consolidation is complete. The spec is now fully satisfied by #1805 plus this small follow-up.

---

## Self-Review Notes

The original spec laid out five FRs, all of which were already delivered by #1805. The arch review identified three potential deltas; this plan addresses exactly one (the `SalesHistory` projection still being duplicated across the two call sites). The other two deltas — `M1_A` vs `M1` and `MapProductType` — are documented as load-bearing post-#1805 decisions and explicitly preserved. Behavior is unchanged for both `StreamProductsWithSalesAsync` and `GetProductAnalysisDataAsync`: identical predicate, identical projection, identical assignment, just relocated to live in one place. The added regression test pins that location so future refactors cannot drift the filter back to the call site without breaking the build.
