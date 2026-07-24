# Design: Extract IStockAnalysisCalculator from GetPurchaseStockAnalysisHandler

## Component Design

### `IStockAnalysisCalculator` (new interface)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs`
- **Responsibility:** Contract for the two pure stock-analysis calculations currently embedded in `GetPurchaseStockAnalysisHandler`. Mirrors `IStockSeverityCalculator` in style: namespace `Anela.Heblo.Application.Features.Purchase.Services`, XML doc summary on the interface and on each parameter, no implementation.
- **Contract:**
  ```csharp
  public interface IStockAnalysisCalculator
  {
      double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);
      double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);
  }
  ```

### `StockAnalysisCalculator` (new implementation)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs`
- **Responsibility:** Stateless implementation of `IStockAnalysisCalculator`. No constructor dependencies, no injected services, no I/O — a plain class matching `StockSeverityCalculator`'s shape.
- **Behavior:** Method bodies are moved verbatim (byte-for-byte) from the handler's current private methods (`GetPurchaseStockAnalysisHandler.cs:137` and `:166`). No logic changes, no added validation, no reformatting of algorithm structure.
  - `CalculateStockEfficiency`: returns `(availableStock / optimalStock) * 100` when `optimalStock > 0`; falls back to `(availableStock / minStock) * 100` when `minStock > 0`; otherwise returns `0`.
  - `CalculateRecommendedOrderQuantity`: returns `null` when both `optimalStock` and `minStock` are `<= 0`; computes `targetStock` (optimal if positive, else `minStock * 2`); returns `null` if `needed = targetStock - availableStock <= 0`; otherwise returns `max(needed, moq)` when `moq` is a parseable positive number, else returns `needed`.

### `GetPurchaseStockAnalysisHandler` (modified)
- **Responsibility change:** Loses the two calculation method bodies; gains a new constructor dependency `IStockAnalysisCalculator stockAnalysisCalculator`, stored as `private readonly IStockAnalysisCalculator _stockAnalysisCalculator`.
- **Field/parameter ordering:** Placed after `_stockSeverityCalculator` and before `_logger`, matching the existing convention (`_materialCatalog`, `_stockSeverityCalculator`, `_stockAnalysisCalculator`, `_logger`).
- **Call sites:** Within `AnalyzeStockItem`, the two existing call sites (previously calling the private methods directly) are updated to `_stockAnalysisCalculator.CalculateStockEfficiency(...)` and `_stockAnalysisCalculator.CalculateRecommendedOrderQuantity(...)`, passing the exact same arguments as before.
- **Unchanged:** `Handle`, `GetLastPurchaseInfo`, `ShouldIncludeItem`, `SortItems`, `CalculateSummary`, and the overall `AnalyzeStockItem` structure aside from the two delegated call sites.

### `PurchaseModule` (modified)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs`
- **Change:** One new DI registration adjacent to the existing `IStockSeverityCalculator` registration (near line 25):
  ```csharp
  services.AddScoped<IStockAnalysisCalculator, StockAnalysisCalculator>();
  ```
- **Lifetime:** `Scoped`, matching `IStockSeverityCalculator`, for consistency rather than technical necessity (the class is stateless).

### Test components (new/modified)

#### `StockAnalysisCalculatorTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Purchase/StockAnalysisCalculatorTests.cs`
- **Structure:** xUnit `[Fact]` tests, FluentAssertions, single class-level `_calculator` field built via `new StockAnalysisCalculator()` — no mocking, mirrors `StockSeverityCalculatorTests.cs` structure and naming convention (`Method_WhenCondition_ReturnsExpectation`).
- **Coverage — `CalculateStockEfficiency`:**
  - `optimalStock > 0` → returns `(availableStock / optimalStock) * 100`.
  - `optimalStock <= 0` and `minStock > 0` → returns `(availableStock / minStock) * 100`.
  - `optimalStock <= 0` and `minStock <= 0` → returns `0`.
- **Coverage — `CalculateRecommendedOrderQuantity`:**
  - `optimalStock <= 0` and `minStock <= 0` → returns `null`.
  - `optimalStock > 0`, `availableStock >= optimalStock` (needed `<= 0`) → returns `null`.
  - `optimalStock <= 0`, `minStock > 0`, target = `minStock * 2`, stock below target → returns the shortfall.
  - `moq` present/parseable, shortfall less than `moq` → returns the parsed `moq` value.
  - `moq` present/parseable, shortfall greater than `moq` → returns the shortfall (not `moq`).
  - `moq` null/empty/unparseable → returns the raw shortfall, ignoring `moq`.

#### `GetPurchaseStockAnalysisHandlerTests` / `GetPurchaseStockAnalysisHandlerDiacriticsTests` (modified — wiring only)
- **Change:** Constructor calls updated to pass a **real** `new StockAnalysisCalculator()` instance — not a `Mock<IStockAnalysisCalculator>`. This is required (not stylistic): an un-stubbed `Mock<IStockAnalysisCalculator>` returns `0.0`/`null` for every call, which would collapse `Handle_SortByStockEfficiency_ReturnsSortedItems` into asserting an order over an all-zero list, trivially passing without exercising real sort behavior.
- `IStockSeverityCalculator` wiring in these tests is untouched — it keeps its existing per-test `.Setup()`/mock style, which is appropriate because those tests need controlled severity outcomes independent of threshold math.
- No test assertions are weakened, removed, or added to existing cases — only constructor wiring changes.

## Data Schemas

No data model, DTO, persistence, or API contract changes. `IStockAnalysisCalculator` operates purely on primitives already validated/produced upstream by the handler:

**`CalculateStockEfficiency`**
| Parameter | Type | Description |
|---|---|---|
| `availableStock` | `double` | Current available stock quantity |
| `minStock` | `double` | Configured minimum stock threshold |
| `optimalStock` | `double` | Configured optimal stock threshold |
| Returns | `double` | Efficiency percentage (`0` when no valid basis exists) |

**`CalculateRecommendedOrderQuantity`**
| Parameter | Type | Description |
|---|---|---|
| `availableStock` | `double` | Current available stock quantity |
| `optimalStock` | `double` | Configured optimal stock threshold |
| `minStock` | `double` | Configured minimum stock threshold |
| `moq` | `string` | Minimum order quantity, as configured (may be null/empty/unparseable) |
| Returns | `double?` | Recommended order quantity, or `null` when no order is needed |

`GetPurchaseStockAnalysisRequest`/`GetPurchaseStockAnalysisResponse` and the underlying HTTP endpoint contract are unaffected by this change.
