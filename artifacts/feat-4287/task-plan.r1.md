# Deduplicate sort-direction ternary in GetProductMarginsHandler.ApplySorting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the 15 repeated `sortDescending ? OrderByDescending(...) : OrderBy(...)` occurrences in `GetProductMarginsHandler.ApplySorting` into single-line calls to one new private static `SortBy<TKey>` helper, with no behavior change.

**Architecture:** Add a private static `SortBy<TKey>(IEnumerable<CatalogAggregate> items, Func<CatalogAggregate, TKey> key, bool desc)` helper to `GetProductMarginsHandler`, then rewrite every arm of `ApplySorting`'s switch expression (and the pre-switch null-guard) to call it, copying each existing selector lambda verbatim. The helper stays private to this class — it is intentionally not shared with the structurally similar `TopProductSorter` (Analytics module), which is out of scope for this change.

**Tech Stack:** .NET 8, C# (LINQ `OrderBy`/`OrderByDescending`), xUnit + FluentAssertions + Moq (existing test project `Anela.Heblo.Tests`).

---

## File Structure

- **Modify:** `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — add `SortBy<TKey>` helper, rewrite `ApplySorting` (lines 129–186 currently).
- **Modify:** `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — add two characterization tests locking in current sort behavior for an "amount" field (`m0amount`) and a "percentage" field (`m0percentage`), the two selector shapes most at risk of a transcription mistake during the mechanical rewrite (per architecture review risk analysis). These are added and passing **before** the refactor, then re-verified **after**.

No other files change. `GetProductMarginsRequest`, `GetProductMarginsResponse`, `CatalogAggregate`, `MarginData`, `MarginLevel`, `MonthlyMarginHistory` are all read-only inputs/outputs and are not touched.

---

### task: add-characterization-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`

- [ ] **Step 1: Write two characterization tests for margin-field sorting**

Add the following two `[Fact]` methods to `GetProductMarginsHandlerTests`, placed after the existing `Handle_UnknownSortField_FallsBackToProductCodeAscending` test (around line 210, right before the closing brace of the class and the `BuildAggregate` helper):

```csharp
[Fact]
public async Task Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount()
{
    // Arrange
    _timeProviderMock
        .Setup(tp => tp.GetUtcNow())
        .Returns(new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero));

    var catalogItems = new[]
    {
        BuildAggregateWithM0Margin(productCode: "LOW001", m0Amount: 10m, m0Percentage: 5m),
        BuildAggregateWithM0Margin(productCode: "HIGH001", m0Amount: 90m, m0Percentage: 45m),
        BuildAggregateWithM0Margin(productCode: "MID001", m0Amount: 50m, m0Percentage: 25m),
    };

    _catalogRepositoryMock
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(catalogItems);

    var request = new GetProductMarginsRequest
    {
        ProductType = ProductType.Product,
        SortBy = "m0amount",
        SortDescending = true,
        PageNumber = 1,
        PageSize = 100
    };

    // Act
    var response = await _handler.Handle(request, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    response.Items.Select(i => i.ProductCode)
        .Should().ContainInOrder("HIGH001", "MID001", "LOW001");
}

[Fact]
public async Task Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage()
{
    // Arrange
    _timeProviderMock
        .Setup(tp => tp.GetUtcNow())
        .Returns(new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero));

    var catalogItems = new[]
    {
        BuildAggregateWithM0Margin(productCode: "LOW001", m0Amount: 10m, m0Percentage: 5m),
        BuildAggregateWithM0Margin(productCode: "HIGH001", m0Amount: 90m, m0Percentage: 45m),
        BuildAggregateWithM0Margin(productCode: "MID001", m0Amount: 50m, m0Percentage: 25m),
    };

    _catalogRepositoryMock
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(catalogItems);

    var request = new GetProductMarginsRequest
    {
        ProductType = ProductType.Product,
        SortBy = "m0percentage",
        SortDescending = false,
        PageNumber = 1,
        PageSize = 100
    };

    // Act
    var response = await _handler.Handle(request, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    response.Items.Select(i => i.ProductCode)
        .Should().ContainInOrder("LOW001", "MID001", "HIGH001");
}
```

Add the corresponding helper next to the existing `BuildAggregate` private static method (same class, just above or below it):

```csharp
private static CatalogAggregate BuildAggregateWithM0Margin(string productCode, decimal m0Amount, decimal m0Percentage)
{
    var aggregate = new CatalogAggregate
    {
        Id = productCode,
        ProductName = "Test Product",
        Type = ProductType.Product
    };

    aggregate.Margins.MonthlyData[new DateTime(2026, 1, 1)] = new MarginData
    {
        M0 = new MarginLevel(m0Percentage, m0Amount, 0m, 0m)
    };

    return aggregate;
}
```

`MonthlyMarginHistory.Averages` (accessed via `CatalogAggregate.Margins.Averages.M0`) averages across `MonthlyData` entries; with exactly one entry per aggregate, the average equals the single value supplied, so `m0Amount`/`m0Percentage` above map directly to `Margins.Averages.M0.Amount`/`.Percentage`.

- [ ] **Step 2: Run the new tests to confirm they already pass against the current (pre-refactor) code**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"
```
Expected: All tests in `GetProductMarginsHandlerTests` PASS, including the two new ones (`Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount`, `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage`). This confirms the tests correctly characterize existing behavior before any refactor — they are not meant to fail here.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
git commit -m "test(catalog): add characterization tests for margin-field sorting in GetProductMarginsHandler

Locks in current m0amount/m0percentage sort behavior before extracting
the SortBy helper in GetProductMarginsHandler.ApplySorting (#4287).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BTCy4YuKrWUcyza9b4ye1Z"
```

---

### task: extract-sortby-helper-and-refactor-applysorting

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs:129-186`

- [ ] **Step 1: Replace the `ApplySorting` method body and add the `SortBy<TKey>` helper**

Replace the entire existing `ApplySorting` method (current lines 129–186, from `private IEnumerable<CatalogAggregate> ApplySorting(...)` through its closing `}`) with the following — the new helper plus the rewritten method:

```csharp
    private static IOrderedEnumerable<CatalogAggregate> SortBy<TKey>(
        IEnumerable<CatalogAggregate> items,
        Func<CatalogAggregate, TKey> key,
        bool desc)
        => desc ? items.OrderByDescending(key) : items.OrderBy(key);

    private IEnumerable<CatalogAggregate> ApplySorting(IEnumerable<CatalogAggregate> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by ProductCode
            return SortBy(products, x => x.ProductCode, sortDescending);
        }

        return sortBy.ToLower() switch
        {
            "productcode" => SortBy(products, x => x.ProductCode, sortDescending),
            "productname" => SortBy(products, x => x.ProductName, sortDescending),
            "pricewithoutvat" => SortBy(products, x => x.PriceWithoutVat ?? 0, sortDescending),
            "purchaseprice" => SortBy(products, x => x.ErpPrice?.PurchasePrice ?? 0, sortDescending),
            "manufacturedifficulty" => SortBy(products, x => x.ManufactureDifficulty ?? 0, sortDescending),
            // M0-M3 margin levels - amounts (using pre-calculated data)
            "m0amount" => SortBy(products, x => x.Margins.Averages.M0.Amount, sortDescending),
            "m1amount" => SortBy(products, x => x.Margins.Averages.M1.Amount, sortDescending),
            "m2amount" => SortBy(products, x => x.Margins.Averages.M2.Amount, sortDescending),
            "m3amount" => SortBy(products, x => x.Margins.Averages.M3.Amount, sortDescending),
            // M0-M3 margin levels - percentages (using pre-calculated data)
            "m0percentage" => SortBy(products, x => x.Margins.Averages.M0.Percentage, sortDescending),
            "m1percentage" => SortBy(products, x => x.Margins.Averages.M1.Percentage, sortDescending),
            "m2percentage" => SortBy(products, x => x.Margins.Averages.M2.Percentage, sortDescending),
            "m3percentage" => SortBy(products, x => x.Margins.Averages.M3.Percentage, sortDescending),
            _ => SortBy(products, x => x.ProductCode, sortDescending)
        };
    }
```

Double-check after pasting: every selector lambda (`x => x.ProductCode`, `x => x.PriceWithoutVat ?? 0`, `x => x.ErpPrice?.PurchasePrice ?? 0`, `x => x.ManufactureDifficulty ?? 0`, `x => x.Margins.Averages.M{0-3}.{Amount,Percentage}`) matches exactly what was in the corresponding arm before this change — no selector expression should differ from the pre-refactor code.

- [ ] **Step 2: Build**

Run:
```bash
cd backend
dotnet build
```
Expected: Build succeeds with no new errors or warnings. (If `SortBy`'s generic type argument fails to infer for any arm, the compiler error will point at the exact line — this should not happen since each selector's return type is unchanged from the original ternary.)

- [ ] **Step 3: Run the full Catalog test suite to confirm no behavior changed**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"
```
Expected: All tests PASS — the pre-existing tests (default sort, product-type filter, unknown-sort-field fallback, monthly-history filtering) and the two characterization tests added in the previous task (`Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount`, `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage`) all PASS unchanged.

- [ ] **Step 4: Run the full backend test suite and format check**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test
```
Expected: `dotnet build` succeeds; `dotnet format --verify-no-changes` reports no formatting issues (if it does, run `dotnet format` without `--verify-no-changes` to apply the fix, then re-stage); the full test suite passes with no new failures.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs
git commit -m "refactor(catalog): extract SortBy helper to deduplicate sort-direction ternary in GetProductMarginsHandler

ApplySorting repeated the sortDescending ? OrderByDescending : OrderBy
ternary once per sort field (15 occurrences: 13 named arms + default +
the pre-switch null-guard). Extract a private static SortBy<TKey>
helper so each site becomes a single line. No behaviour change — every
selector lambda is copied verbatim from the arm it replaces.

Fixes #4287

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01BTCy4YuKrWUcyza9b4ye1Z"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (extract shared helper) → covered by `extract-sortby-helper-and-refactor-applysorting` Step 1 (adds `SortBy<TKey>`).
- FR-2 (rewrite every arm to call the helper) → covered by the same step's full replacement of `ApplySorting`, including the default arm and pre-switch null-guard.
- FR-3 (no behavior change) → covered by `add-characterization-tests` (locks in behavior before the change) and `extract-sortby-helper-and-refactor-applysorting` Steps 3–4 (re-verifies all tests, including the new characterization tests, after the change).
- NFR-1 (performance / deferred execution preserved) → satisfied structurally: the helper is a direct pass-through to `OrderBy`/`OrderByDescending`, no eager enumeration introduced.
- NFR-2 (security) → not applicable, no task needed.
- "Data Model" / "API / Interface Design" (no changes) → satisfied by construction: no task touches `GetProductMarginsRequest`, `GetProductMarginsResponse`, or `CatalogAggregate`.
- "Out of Scope" items (no field changes, no `TopProductSorter` changes, no other handler changes) → respected: only the two listed files are modified.

**2. Placeholder scan:** No "TBD"/"TODO"/"add appropriate handling" placeholders. All code blocks are complete, copy-pasteable C#. No step says "similar to Task N" — each task's full code is shown.

**3. Type consistency:** `SortBy<TKey>` is defined once in `extract-sortby-helper-and-refactor-applysorting` and every call site in the same task's code block uses the same name, generic pattern, and parameter order (`items, key, desc` positionally; called as `SortBy(products, x => ..., sortDescending)` everywhere). `BuildAggregateWithM0Margin` is defined once in `add-characterization-tests` and both new tests in that same task call it with the same 3-parameter signature (`productCode, m0Amount, m0Percentage`).
