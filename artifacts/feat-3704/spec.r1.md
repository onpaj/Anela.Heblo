# Specification: Extract IStockAnalysisCalculator from GetPurchaseStockAnalysisHandler

## Summary
`GetPurchaseStockAnalysisHandler` currently implements two business calculations (`CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity`) as private methods, while an analogous calculation (`DetermineStockSeverity`) was already extracted into an injectable `IStockSeverityCalculator` service. This refactor extracts the two remaining calculations into a new `IStockAnalysisCalculator` service, following the exact structural pattern of `IStockSeverityCalculator`/`StockSeverityCalculator`, so all three related stock calculations are consistently testable and the handler is reduced to orchestration.

## Background
This task originates from a filed arch-review finding (`artifacts/feat-3704/brief.md`), not a user-facing feature request. The finding observes that `GetPurchaseStockAnalysisHandler` (`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`) mixes orchestration (loading snapshots, filtering, sorting, paging, summarizing) with embedded business-logic calculations. `IStockSeverityCalculator` already exists as a precedent: it was extracted for exactly this reason and is injected into the handler and unit-tested independently in `StockSeverityCalculatorTests.cs`. The two remaining calculations, `CalculateStockEfficiency` (line 137) and `CalculateRecommendedOrderQuantity` (line 166), were not given the same treatment, creating an inconsistency and a testability gap: today they can only be exercised indirectly through full handler tests that must wire up `IMaterialCatalogService`, `IStockSeverityCalculator`, and a logger.

This is a pure maintainability refactor. It must not change observable behavior, response shape, or any calculation output.

## Functional Requirements

### FR-1: Create `IStockAnalysisCalculator` interface
Add a new interface `IStockAnalysisCalculator` in `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`, mirroring the style of `IStockSeverityCalculator.cs` (namespace, XML doc comments per parameter, no implementation).

Signature:
```csharp
public interface IStockAnalysisCalculator
{
    double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);
    double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);
}
```

**Acceptance criteria:**
- File located at `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`.
- Interface and both methods carry XML doc summaries consistent with `IStockSeverityCalculator`'s documentation style.
- Method signatures match the brief exactly (parameter names, types, order, nullability of the return type on the second method).

### FR-2: Implement `StockAnalysisCalculator`
Add `StockAnalysisCalculator : IStockAnalysisCalculator` in `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`, mirroring `StockSeverityCalculator.cs` (plain class, no injected dependencies, XML doc comments).

The method bodies must be moved verbatim (byte-for-byte logic, not reworded) from the handler's current private implementations:

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

**Acceptance criteria:**
- File located at `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`.
- Logic is copied unchanged from the handler — no behavioral edits, no reformatting of the algorithm, no added validation.
- Class has no constructor dependencies (matches `StockSeverityCalculator`, which is stateless).

### FR-3: Update `GetPurchaseStockAnalysisHandler` to delegate
Modify `GetPurchaseStockAnalysisHandler`:
- Add a constructor parameter `IStockAnalysisCalculator stockAnalysisCalculator`, stored in a new `private readonly IStockAnalysisCalculator _stockAnalysisCalculator` field, placed alongside the existing `_stockSeverityCalculator` field/parameter (same ordering convention: after `_materialCatalog`, before `_logger`).
- Remove the private `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` methods from the handler entirely.
- Update the two call sites inside `AnalyzeStockItem` (currently lines 102 and 107) to call `_stockAnalysisCalculator.CalculateStockEfficiency(...)` and `_stockAnalysisCalculator.CalculateRecommendedOrderQuantity(...)` with the exact same arguments currently passed.

**Acceptance criteria:**
- Handler no longer contains `CalculateStockEfficiency` or `CalculateRecommendedOrderQuantity` method bodies.
- Handler compiles and calls both calculations through `_stockAnalysisCalculator`.
- No change to any other handler method (`Handle`, `AnalyzeStockItem`'s structure aside from the two call sites, `GetLastPurchaseInfo`, `ShouldIncludeItem`, `SortItems`, `CalculateSummary`) — these are explicitly out of scope (see Out of Scope).

### FR-4: Register the new service in DI
In `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs`, register `IStockAnalysisCalculator` with the same lifetime as `IStockSeverityCalculator`:

```csharp
services.AddScoped<IStockAnalysisCalculator, StockAnalysisCalculator>();
```

Place it immediately adjacent to the existing `IStockSeverityCalculator` registration (line 25), keeping the `// Register stock severity calculator` comment accurate or extending it to cover both, per the developer's judgment at implementation time — either a single combined comment or two adjacent registrations with their own comments is acceptable.

**Acceptance criteria:**
- `AddPurchaseModule` registers `IStockAnalysisCalculator` as `Scoped`, matching `IStockSeverityCalculator`'s lifetime.
- Application starts and resolves `GetPurchaseStockAnalysisHandler` without DI errors (verified by existing handler tests / integration test host, if any, still passing).

### FR-5: Unit tests for `StockAnalysisCalculator`
Add `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`, following the exact structure of `StockSeverityCalculatorTests.cs` (xUnit `[Fact]` tests, FluentAssertions, one class-level `_calculator` instance built directly via `new StockAnalysisCalculator()`, no mocking needed since the service is stateless).

Required coverage for `CalculateStockEfficiency`:
- `optimalStock > 0`: returns `(availableStock / optimalStock) * 100`.
- `optimalStock <= 0` and `minStock > 0`: returns `(availableStock / minStock) * 100`.
- `optimalStock <= 0` and `minStock <= 0`: returns `0`.

Required coverage for `CalculateRecommendedOrderQuantity`:
- `optimalStock <= 0` and `minStock <= 0`: returns `null`.
- `optimalStock > 0`, `availableStock >= optimalStock` (needed <= 0): returns `null`.
- `optimalStock <= 0`, `minStock > 0`, target = `minStock * 2`, stock below target: returns the shortfall.
- `moq` present and parseable, shortfall less than `moq`: returns the parsed `moq` value (MOQ rounding up).
- `moq` present and parseable, shortfall greater than `moq`: returns the shortfall (not the MOQ).
- `moq` null/empty or unparseable: returns the raw shortfall, ignoring `moq`.

**Acceptance criteria:**
- All listed cases exist as distinct `[Fact]` tests with descriptive names following the `Method_WhenCondition_ReturnsExpectation` convention used in `StockSeverityCalculatorTests.cs`.
- Tests pass under `dotnet test` for the `Anela.Heblo.Tests` project.
- No existing test file is modified to accommodate these new tests (new file only).

### FR-6: Preserve existing handler test behavior
`GetPurchaseStockAnalysisHandlerTests.cs` and `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` (existing test files under `backend/test/Anela.Heblo.Tests/Features/Purchase/`) currently construct `GetPurchaseStockAnalysisHandler` directly. Their constructor calls must be updated to pass an `IStockAnalysisCalculator` instance (a real `StockAnalysisCalculator`, mirroring how they currently pass a real or mocked `IStockSeverityCalculator` — match whichever style those tests already use for `IStockSeverityCalculator`).

**Acceptance criteria:**
- Both existing test files compile and all their existing test cases continue to pass unmodified in assertions/expectations — only the constructor wiring changes.
- No test assertions are weakened, removed, or skipped to make this pass.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected or targeted — this is a structural extraction of pure, side-effect-free arithmetic. The new service must remain synchronous and allocation-free beyond what the original private methods did.

### NFR-2: Security
No security surface change. No new data access, no new external calls, no new user input handling. Both extracted methods operate solely on primitive `double`/`string` parameters already validated/produced upstream by the handler.

## Data Model
No data model changes. No new DTOs, no persistence changes, no contract/API changes. `IStockAnalysisCalculator` operates purely on primitives (`double`, `string`), exactly like `IStockSeverityCalculator`.

## API / Interface Design
No public API (controller/endpoint) changes. This is an internal application-layer refactor only:
- New interface: `Anela.Heblo.Application.Features.Purchase.Services.IStockAnalysisCalculator`
- New implementation: `Anela.Heblo.Application.Features.Purchase.Services.StockAnalysisCalculator`
- Modified: `GetPurchaseStockAnalysisHandler` constructor gains one new dependency.
- Modified: `PurchaseModule.AddPurchaseModule` gains one new DI registration.

`GetPurchaseStockAnalysisRequest`/`GetPurchaseStockAnalysisResponse` and the underlying HTTP endpoint contract are unaffected.

## Dependencies
- Depends on the existing `IStockSeverityCalculator` pattern as the structural template (interface + implementation + scoped DI registration + dedicated xUnit/FluentAssertions test file) — no new libraries or external services required.
- No dependency on other in-flight features.

## Out of Scope
- `SortItems` extraction/inlining — the brief explicitly flags it as optional ("can remain in the handler or be inlined... it is not business logic") and this task is scoped to the two calculation methods only.
- Any change to `ShouldIncludeItem`, `CalculateSummary`, `GetLastPurchaseInfo`, or the `Handle` orchestration flow.
- Any change to `IStockSeverityCalculator`/`StockSeverityCalculator` itself.
- Any behavioral change to stock efficiency or recommended order quantity calculations — this is a pure move, not a rewrite.
- Any change to the public API contract, response DTOs, or frontend consumption of this endpoint.
- Any change to `MinimalOrderQuantity` parsing/validation elsewhere in the codebase.

## Open Questions
None.

## Status: COMPLETE
