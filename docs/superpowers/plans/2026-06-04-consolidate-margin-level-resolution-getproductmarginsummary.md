# Consolidate Margin-Level Resolution in GetProductMarginSummaryHandler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the duplicated `M0/M1/M2` switch inside `GetProductMarginSummaryHandler.CalculateTotalMarginForLevel` with a delegated call to the already-injected `IMarginCalculator.GetMarginAmountForLevel`, so margin-level resolution lives in exactly one place.

**Architecture:** Behavior-preserving refactor inside one MediatR application handler. No new types, no DI changes, no contract changes. Margin-level resolution becomes a single-source-of-truth concern owned by `IMarginCalculator`; the handler stops re-implementing it. Tests are added at the handler level to lock the existing semantics (case-insensitive resolution + silent fallback to `M2`) before and after the refactor.

**Tech Stack:** C# / .NET 8, xUnit, FluentAssertions, Moq, MediatR. Backend Application layer in the Analytics Vertical Slice.

---

## Context for the Implementer

You are touching exactly **one production file** and **one test file**:

- **Production (modify only):** `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
  - Method to change: `CalculateTotalMarginForLevel` at **lines 217–237**.
- **Tests (add cases to existing class):** `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`
  - Add two new test methods inside the existing `GetProductMarginSummaryHandlerTests` class. Do not restructure the file.

You **must not modify** any of the following:

- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` (the canonical `GetMarginAmountForLevel` at lines 113–125 is the source of truth).
- The `IMarginCalculator` interface.
- Any other handler method (`Handle`, `GenerateTopProducts`, `CalculateGroupMarginData`, `ApplySorting`).
- DI registration, controllers, DTOs, OpenAPI surface, or anything frontend.

The dependency `_marginCalculator` is already injected via the constructor (handler line 15) and already used elsewhere in the same handler — no DI wiring is needed.

The canonical method you delegate to:

```csharp
// MarginCalculator.cs lines 113–125 — DO NOT MODIFY
public decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel)
{
    return marginLevel.ToUpperInvariant() switch
    {
        "M0" => product.M0Amount,
        "M1" => product.M1Amount,
        "M2" => product.M2Amount,
        _ => product.M2Amount // Default to M2
    };
}
```

Because the canonical method has byte-identical behavior to the handler's inline switch, the refactor is purely a delegation cleanup: the resulting numeric output for every `(products, marginLevel)` pair is unchanged.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` | Modify (lines 217–237 only) | Application handler for product margin summary. After the change, its `CalculateTotalMarginForLevel` private helper delegates per-unit margin lookup to `_marginCalculator.GetMarginAmountForLevel`. |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` | Modify (append new test methods to existing class) | Adds two characterization tests that lock the existing semantics: case-insensitive `marginLevel` resolution and silent fallback to `M2` for unknown values. |

No new files, no new folders, no new test project.

---

## Task 1: Add characterization test — case-insensitive `marginLevel` resolution

This test locks in the current behavior (existing inline switch in the handler uses `ToUpperInvariant()`) so that the refactor cannot silently change it. The test must **pass against the current code** (the existing inline switch already handles case insensitivity) **and continue to pass after the refactor** (the canonical method also uses `ToUpperInvariant()`).

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

- [ ] **Step 1: Add the failing test**

Open `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`. Insert this new test method inside the existing `GetProductMarginSummaryHandlerTests` class, immediately **after** the existing `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator` test method and **before** the closing brace of the class (`}` at the original line 281). Match the file's existing using directives — they already include everything we need (`FluentAssertions`, `Moq`, `Xunit`, the Analytics namespaces).

```csharp
[Theory]
[InlineData("M1")]
[InlineData("m1")]
[InlineData("mI")]  // mixed case, still resolves to M1 via ToUpperInvariant
public async Task Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin(string marginLevel)
{
    // Arrange
    var request = new GetProductMarginSummaryRequest
    {
        TimeWindow = "current-year",
        GroupingMode = ProductGroupingMode.Products,
        MarginLevel = marginLevel
    };

    var today = DateTime.Today;
    var fromDate = new DateTime(today.Year, 1, 1);
    var toDate = today;

    var analyticsProducts = new List<AnalyticsProduct>
    {
        new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Product 1",
            Type = AnalyticsProductType.Product,
            MarginAmount = 100m,
            M0Amount = 10m,
            M1Amount = 20m,
            M2Amount = 30m,
            SalesHistory = new List<SalesDataPoint>
            {
                new() { Date = new DateTime(today.Year, 3, 15), AmountB2B = 10, AmountB2C = 5 }
            }
        }
    };

    _analyticsRepositoryMock
        .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
            It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
        .Returns(analyticsProducts.ToAsyncEnumerable());

    // Act
    var result = await _handler.Handle(request, CancellationToken.None);

    // Assert
    // 15 units * 20 (M1Amount) = 300, regardless of marginLevel casing
    result.TopProducts.Should().HaveCount(1);
    result.TopProducts[0].TotalMargin.Should().Be(300m);
}
```

- [ ] **Step 2: Run the new test and verify it PASSES against current code**

The existing inline switch in the handler already uses `ToUpperInvariant()`, so this characterization test should pass right now. That's the point — we are locking the existing behavior before refactoring.

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests.Handle_MarginLevelIsCaseInsensitive" \
  --no-restore
```

Expected: **3 passed** (one per `InlineData`), **0 failed**. If any case fails, stop and investigate — the existing behavior is not what the spec described, and the refactor cannot proceed without a corrected understanding.

- [ ] **Step 3: Commit the new characterization test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs
git commit -m "test: lock case-insensitive marginLevel resolution in GetProductMarginSummaryHandler"
```

---

## Task 2: Add characterization test — unknown `marginLevel` falls back to `M2`

This test locks the existing silent-fallback semantics (`_ => product.M2Amount`). The current inline switch already implements this; the refactor delegates to a method that implements the identical fallback.

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

- [ ] **Step 1: Add the failing test**

Insert this new test method directly **after** the `Handle_MarginLevelIsCaseInsensitive_ProducesIdenticalTotalMargin` test you added in Task 1, still inside the `GetProductMarginSummaryHandlerTests` class:

```csharp
[Theory]
[InlineData("M9")]    // unknown level
[InlineData("")]      // empty string
[InlineData("xyz")]   // arbitrary text
public async Task Handle_UnknownMarginLevel_FallsBackToM2(string unknownMarginLevel)
{
    // Arrange — distinct M0/M1/M2 amounts so the fallback choice is observable
    var today = DateTime.Today;
    var fromDate = new DateTime(today.Year, 1, 1);
    var toDate = today;

    var analyticsProducts = new List<AnalyticsProduct>
    {
        new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Product 1",
            Type = AnalyticsProductType.Product,
            MarginAmount = 100m,
            M0Amount = 10m,
            M1Amount = 20m,
            M2Amount = 30m,
            SalesHistory = new List<SalesDataPoint>
            {
                new() { Date = new DateTime(today.Year, 3, 15), AmountB2B = 10, AmountB2C = 5 }
            }
        }
    };

    _analyticsRepositoryMock
        .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
            It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
        .Returns(analyticsProducts.ToAsyncEnumerable());

    var unknownRequest = new GetProductMarginSummaryRequest
    {
        TimeWindow = "current-year",
        GroupingMode = ProductGroupingMode.Products,
        MarginLevel = unknownMarginLevel
    };

    // Act
    var unknownResult = await _handler.Handle(unknownRequest, CancellationToken.None);

    // Assert
    // 15 units * 30 (M2Amount fallback) = 450, regardless of unknown marginLevel value
    unknownResult.TopProducts.Should().HaveCount(1);
    unknownResult.TopProducts[0].TotalMargin.Should().Be(450m);
}
```

- [ ] **Step 2: Run the new test and verify it PASSES against current code**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests.Handle_UnknownMarginLevel_FallsBackToM2" \
  --no-restore
```

Expected: **3 passed**, **0 failed**. Existing inline switch already falls back to `M2` — characterization is correct.

- [ ] **Step 3: Run the full handler test suite to make sure nothing else broke**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests" \
  --no-restore
```

Expected: all previously passing tests still pass; the two new tests pass. **0 failed**.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs
git commit -m "test: lock M2 fallback for unknown marginLevel in GetProductMarginSummaryHandler"
```

---

## Task 3: Refactor `CalculateTotalMarginForLevel` to delegate to `IMarginCalculator`

This is the actual refactor. Both tests from Tasks 1 and 2 must continue to pass.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs:217-237`

- [ ] **Step 1: Replace the body of `CalculateTotalMarginForLevel`**

Open `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`. Lines 217–237 currently read:

```csharp
/// <summary>
/// Calculates total margin for a group of products based on selected margin level
/// </summary>
private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel)
{
    var totalMargin = 0m;

    foreach (var product in products)
    {
        var totalSales = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);

        var marginPerUnit = marginLevel.ToUpperInvariant() switch
        {
            "M0" => product.M0Amount,
            "M1" => product.M1Amount,
            "M2" => product.M2Amount,
            _ => product.M2Amount // Default to M2 (highest level now)
        };

        totalMargin += (decimal)totalSales * marginPerUnit;
    }

    return totalMargin;
}
```

Replace the entire method (keep the XML `<summary>` line) with this body:

```csharp
/// <summary>
/// Calculates total margin for a group of products based on selected margin level
/// </summary>
private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel)
{
    return products.Sum(p =>
        (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)
        * _marginCalculator.GetMarginAmountForLevel(p, marginLevel));
}
```

Notes:
- The cast `(decimal)` stays on the `SalesHistory.Sum(...)` result — this mirrors the canonical expression at `MarginCalculator.cs:63` (`(decimal)totalSold * GetMarginAmountForLevel(product, marginLevel)`) and preserves operator precedence and numeric semantics exactly.
- Do not change the method signature or accessibility.
- Do not touch any other method.
- Do not remove or reformat the XML doc comment.

- [ ] **Step 2: Verify the inline switch is gone**

Run:

```bash
grep -n '"M0" =>\|"M1" =>\|"M2" =>' \
  backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs
```

Expected: **no output** (the handler no longer contains any `"M0" =>` / `"M1" =>` / `"M2" =>` arms).

Additionally, run the NFR-3 grep:

```bash
grep -rn '"M0" =>' backend/src/Anela.Heblo.Application/Features/Analytics/
```

Expected: **exactly one hit**, inside `MarginCalculator.cs` (around line 120). Any other hit means a duplication was missed.

- [ ] **Step 3: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds, **0 errors**. Warnings should be unchanged from the pre-refactor baseline. If a new warning appears (e.g., unused variable), stop and investigate — the refactor should not introduce warnings.

- [ ] **Step 4: Run the handler test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests" \
  --no-restore
```

Expected: all tests pass, including the two new characterization tests from Tasks 1 and 2. **0 failed**.

If any test fails: the refactor changed behavior. Re-read Step 1 — the most likely culprit is the cast position. The cast must be on `p.SalesHistory.Sum(...)`, not on the whole multiplication, to match the original `(decimal)totalSales * marginPerUnit` shape.

- [ ] **Step 5: Run the calculator test suite (defensive check — should be unaffected)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarginCalculatorTests" \
  --no-restore
```

Expected: all tests pass. The canonical method was not modified, so this is a sanity check.

- [ ] **Step 6: Run the full Analytics test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Analytics" \
  --no-restore
```

Expected: all Analytics tests pass. **0 failed**.

- [ ] **Step 7: Format**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs
```

Expected: clean run. If the formatter changes the file, re-run Step 4 to confirm tests still pass.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs
git commit -m "refactor: delegate margin-level resolution in GetProductMarginSummaryHandler to IMarginCalculator"
```

---

## Task 4: Final validation

A last pass to confirm the success criteria from the spec are all green.

- [ ] **Step 1: Confirm NFR-3 grep guard**

```bash
grep -rn '"M0" =>' backend/src/Anela.Heblo.Application/Features/Analytics/
```

Expected: **exactly one** match line, in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`.

- [ ] **Step 2: Confirm FR-1 — the handler no longer references `M0Amount` / `M1Amount` / `M2Amount` for level resolution**

```bash
grep -n 'M0Amount\|M1Amount\|M2Amount' \
  backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs
```

Expected: references remain only inside `CalculateGroupMarginData` (the aggregated-data method, which is out of scope for this refactor — it computes weighted averages of all three levels simultaneously, not a single-level selection). There should be **no** `M0Amount` / `M1Amount` / `M2Amount` reference inside `CalculateTotalMarginForLevel` anymore.

- [ ] **Step 3: Final full build + test**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: build succeeds, all tests pass.

- [ ] **Step 4: Confirm working tree is clean**

```bash
git status
```

Expected: `nothing to commit, working tree clean`. The branch should now contain three new commits on top of the brief/spec/arch-review commits:

1. `test: lock case-insensitive marginLevel resolution in GetProductMarginSummaryHandler`
2. `test: lock M2 fallback for unknown marginLevel in GetProductMarginSummaryHandler`
3. `refactor: delegate margin-level resolution in GetProductMarginSummaryHandler to IMarginCalculator`

---

## Self-Review (executed when writing this plan)

**Spec coverage:**

| Spec requirement | Task implementing it |
|------------------|----------------------|
| FR-1 (delegate to `IMarginCalculator`) | Task 3, Steps 1–2 |
| FR-1 AC: no `"M0" =>` / `"M1" =>` in handler | Task 3, Step 2 |
| FR-2 (preserve behavior, case-insensitive, unknown → M2 fallback, sales multiplication) | Tasks 1–2 (lock semantics), Task 3 (refactor preserves them), Task 3 Step 4 (re-run) |
| FR-2 AC: unknown-level test | Task 2 |
| FR-2 AC: case-insensitive test | Task 1 |
| FR-3 (do not change `MarginCalculator` or `IMarginCalculator`) | Stated explicitly in *Context for the Implementer*; Task 3 Step 5 sanity-runs `MarginCalculatorTests` |
| NFR-1 (no perf regression > 1%) | Architecture review notes interface dispatch on hot path is negligible — no benchmark added; no perf-sensitive code introduced |
| NFR-2 (no security impact) | Refactor is internal; no boundary changes |
| NFR-3 (exactly one `"M0" =>` after change) | Task 3 Step 2 and Task 4 Step 1 (grep guard) |
| NFR-4 (coverage not reduced) | No existing tests removed; two new tests added |
| Out of scope (no `M3`, no calculator changes, no `IMarginCalculator` widening, no inlining the helper, no codebase-wide duplicate search) | Plan does none of these |

**Placeholder scan:** no `TBD`, `TODO`, "implement later", "appropriate error handling", "similar to Task N" remain. All code blocks contain final code, all commands contain expected outputs.

**Type consistency:** the method signature `private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel)` is identical before and after. The `_marginCalculator` field name matches the constructor at line 24. `IMarginCalculator.GetMarginAmountForLevel(AnalyticsProduct, string)` signature matches `MarginCalculator.cs:116`. All test data uses the existing `AnalyticsProduct` shape from the existing test file (`ProductCode`, `ProductName`, `Type`, `MarginAmount`, `M0Amount`, `M1Amount`, `M2Amount`, `SalesHistory` of `SalesDataPoint`).
