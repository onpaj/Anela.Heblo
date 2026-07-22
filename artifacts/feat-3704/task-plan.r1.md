# Implementation Plan: Extract IStockAnalysisCalculator from GetPurchaseStockAnalysisHandler

## Overview
`GetPurchaseStockAnalysisHandler` currently embeds two pure calculation methods (`CalculateStockEfficiency`, `CalculateRecommendedOrderQuantity`) as private methods, while a structurally identical calculation (`DetermineStockSeverity`) already lives in an injectable `IStockSeverityCalculator` service. This is a single, tightly-scoped Extract Service refactor: create `IStockAnalysisCalculator`/`StockAnalysisCalculator` following `IStockSeverityCalculator`/`StockSeverityCalculator` exactly, move the two method bodies verbatim, wire it into the handler and DI, update existing handler tests' constructor wiring, and add a dedicated unit test file. No behavior, API contract, or DTO changes. This is scoped as one cohesive task rather than split further, per the arch-review's explicit guidance against fragmenting it.

### task: extract-stock-analysis-calculator

**Goal:** Extract `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` out of `GetPurchaseStockAnalysisHandler` into a new `IStockAnalysisCalculator` service, mirroring the existing `IStockSeverityCalculator` pattern exactly. Pure move — no behavioral changes, no API/DTO changes.

**Reference pattern to mirror exactly:**
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs`

**1. Create `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`**

Namespace `Anela.Heblo.Application.Features.Purchase.Services`. Interface with XML doc summary on the interface and on each parameter (match `IStockSeverityCalculator`'s doc style). No implementation.

```csharp
public interface IStockAnalysisCalculator
{
    double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);
    double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);
}
```

Signature (parameter names, order, nullability) must match exactly as written above.

**2. Create `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`**

Namespace `Anela.Heblo.Application.Features.Purchase.Services`. Plain class `StockAnalysisCalculator : IStockAnalysisCalculator`, no constructor, no injected dependencies (stateless, matches `StockSeverityCalculator`). XML doc comments matching the interface's style.

Move the method bodies **verbatim** (byte-for-byte, no rewording, no reformatting, no added validation) from `GetPurchaseStockAnalysisHandler.cs` (currently around line 137 for `CalculateStockEfficiency`, line 166 for `CalculateRecommendedOrderQuantity`):

```csharp
public double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock)
{
    if (optimalStock <= 0)
    {
        return minStock > 0 ? (availableStock / minStock) * 100 : 0;
    }

    return (availableStock / optimalStock) * 100;
}

public double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq)
{
    if (optimalStock <= 0 && minStock <= 0)
    {
        return null;
    }

    var targetStock = optimalStock > 0 ? optimalStock : minStock * 2;
    var needed = targetStock - availableStock;

    if (needed <= 0)
    {
        return null;
    }

    if (!string.IsNullOrEmpty(moq) && double.TryParse(moq, out var minOrderQty))
    {
        return Math.Max(needed, minOrderQty);
    }

    return needed;
}
```

**3. Modify `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`**

- Add constructor parameter `IStockAnalysisCalculator stockAnalysisCalculator`, stored as `private readonly IStockAnalysisCalculator _stockAnalysisCalculator`. Field/parameter ordering: after `_materialCatalog` and `_stockSeverityCalculator`, before `_logger` (i.e. order is `_materialCatalog`, `_stockSeverityCalculator`, `_stockAnalysisCalculator`, `_logger`).
- Remove the private `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` methods entirely from the handler.
- Update the two call sites inside `AnalyzeStockItem` (currently around lines 102 and 107) from direct private-method calls to `_stockAnalysisCalculator.CalculateStockEfficiency(...)` and `_stockAnalysisCalculator.CalculateRecommendedOrderQuantity(...)`, passing the exact same arguments as today.
- Do not touch `Handle`, `GetLastPurchaseInfo`, `ShouldIncludeItem`, `SortItems`, `CalculateSummary`, or any other part of `AnalyzeStockItem` beyond the two call sites.

**4. Modify `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs`**

Add, immediately adjacent to the existing `IStockSeverityCalculator` registration (near line 25):

```csharp
services.AddScoped<IStockAnalysisCalculator, StockAnalysisCalculator>();
```

Same `Scoped` lifetime as `IStockSeverityCalculator`. Keep the existing `// Register stock severity calculator` comment accurate — either extend it to cover both registrations or add a second comment; either is fine.

**5. Create `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`**

Mirror `StockSeverityCalculatorTests.cs` structure exactly: xUnit `[Fact]` tests, FluentAssertions, a single class-level `_calculator` field constructed via `new StockAnalysisCalculator()` (no mocking — stateless service). Test naming convention: `Method_WhenCondition_ReturnsExpectation`.

Required test cases for `CalculateStockEfficiency`:
- `optimalStock > 0` → returns `(availableStock / optimalStock) * 100`.
- `optimalStock <= 0` and `minStock > 0` → returns `(availableStock / minStock) * 100`.
- `optimalStock <= 0` and `minStock <= 0` → returns `0`.

Required test cases for `CalculateRecommendedOrderQuantity`:
- `optimalStock <= 0` and `minStock <= 0` → returns `null`.
- `optimalStock > 0`, `availableStock >= optimalStock` (needed `<= 0`) → returns `null`.
- `optimalStock <= 0`, `minStock > 0`, target = `minStock * 2`, stock below target → returns the shortfall.
- `moq` present and parseable, shortfall less than `moq` → returns the parsed `moq` value.
- `moq` present and parseable, shortfall greater than `moq` → returns the shortfall (not `moq`).
- `moq` null/empty/unparseable → returns the raw shortfall, ignoring `moq`.

Each bullet is a distinct `[Fact]`. This is a new file only — do not modify `StockSeverityCalculatorTests.cs`.

**6. Modify existing handler tests — constructor wiring only**

Files: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` and `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`. Both currently construct `GetPurchaseStockAnalysisHandler` directly and will fail to compile once the constructor gains a new parameter.

Update both constructor call sites to pass a **real** `new StockAnalysisCalculator()` instance — **do not** use `Mock<IStockAnalysisCalculator>`. This is a hard requirement, not a style choice: an un-stubbed `Mock<IStockAnalysisCalculator>` returns `0.0`/`null` for every call (Moq default), which would collapse `Handle_SortByStockEfficiency_ReturnsSortedItems` (in `GetPurchaseStockAnalysisHandlerTests.cs`, currently around lines 200-221) into asserting an order over an all-zero list — trivially passing without exercising real sort behavior. This differs deliberately from how `IStockSeverityCalculator` is wired in these same tests (which stays mocked with per-test `.Setup()` calls, since severity tests need controlled outcomes independent of threshold math — leave that wiring untouched).

Do not modify any test assertions, expectations, or add/remove/skip any existing test cases — only the constructor argument list changes.

**Verification / acceptance criteria for this task:**
- `dotnet build` succeeds for the whole solution.
- `dotnet format` produces no diffs (or is applied).
- Handler no longer contains `CalculateStockEfficiency` or `CalculateRecommendedOrderQuantity` method bodies; both are only referenced via `_stockAnalysisCalculator`.
- `PurchaseModule.AddPurchaseModule` registers `IStockAnalysisCalculator` as `Scoped`.
- `dotnet test` passes for `Anela.Heblo.Tests`, including: the new `StockAnalysisCalculatorTests`, the existing `GetPurchaseStockAnalysisHandlerTests` (all cases, including `Handle_SortByStockEfficiency_ReturnsSortedItems` still exercising real computed values), and the existing `GetPurchaseStockAnalysisHandlerDiacriticsTests`.
- No changes to `GetPurchaseStockAnalysisRequest`/`GetPurchaseStockAnalysisResponse`, the HTTP endpoint contract, `IStockSeverityCalculator`/`StockSeverityCalculator`, or `SortItems`/`ShouldIncludeItem`/`CalculateSummary`/`GetLastPurchaseInfo`.
