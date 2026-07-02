# Design: Test Coverage – GetPurchaseStockAnalysis Dual-Bucket Invariant and Summary Assertions

## Component Design

### Target: `GetPurchaseStockAnalysisHandlerTests`

**File:** `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`

Two new `[Fact]` methods are appended to the existing `GetPurchaseStockAnalysisHandlerTests` class. No new files, classes, or mocks are introduced — all infrastructure (`_materialCatalogMock`, `_stockSeverityCalculatorMock`, `_loggerMock`, `_handler`, `MakeSnapshot`) is already present.

#### FR-1: `Handle_FilterByCriticalStatus_SummaryReflectsAllItems`

Verifies the dual-bucket invariant: `response.Items` is filtered by severity while `response.Summary` is computed over the full unfiltered population.

**Collaborators used:**
- `_materialCatalogMock.GetStockAnalysisSnapshotsAsync` — returns 4 snapshots (2 Critical + 2 Optimal)
- `_stockSeverityCalculatorMock.DetermineStockSeverity` via `SetupSequence` — returns Critical, Critical, Optimal, Optimal in call order

**Invariant under test:** The handler calls `CalculateSummary` on `allAnalysisItems` (all 4) before applying the `StockStatus = Critical` display filter, so `Summary.TotalProducts` and `Summary.OptimalCount` must reflect the full population even though `Items` contains only 2 Critical entries.

#### FR-2: `Handle_CalculateSummary_AllFieldsAreCorrect`

Pins every field of `StockAnalysisSummaryDto` to exact expected values derived from a controlled 5-item snapshot.

**Collaborators used:**
- `_materialCatalogMock.GetStockAnalysisSnapshotsAsync` — returns 5 snapshots with precisely controlled `available` and `UnitPrice` values
- `_stockSeverityCalculatorMock.DetermineStockSeverity` via `SetupSequence` — returns Critical, Low, Optimal, Overstocked, NotConfigured in call order

**Constraints enforced by the arch review:**
- All items use `ordered = 0` so `EffectiveStock = available` (avoids ambiguity in `TotalInventoryValue` formula)
- The NotConfigured item has `lastPurchase = null` (contributes 0 to `TotalInventoryValue`)
- `FromDate`/`ToDate` are pinned UTC literals (`new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc)`) — never `DateTime.UtcNow`
- Expected `TotalInventoryValue` is a hard-coded decimal literal with an inline comment showing the arithmetic; it is never computed at runtime

### Handler logic being exercised (read-only, no changes)

`GetPurchaseStockAnalysisHandler.Handle` in
`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`:

- `AnalyzeStockItem` calls `_stockSeverityCalculator.DetermineStockSeverity` once per snapshot, in list order — this is what `SetupSequence` exploits
- `CalculateSummary` operates on `allAnalysisItems` (pre-filter), computing `TotalInventoryValue` as `Sum(EffectiveStock * (LastPurchase?.UnitPrice ?? 0))`
- Display filter (`ShouldIncludeItem`) runs after summary calculation

## Data Schemas

### `GetPurchaseStockAnalysisRequest` — relevant fields for both tests

| Field | FR-1 value | FR-2 value |
|---|---|---|
| `StockStatus` | `StockStatusFilter.Critical` | `StockStatusFilter.All` |
| `PageNumber` | `1` | `1` |
| `PageSize` | `10` | `10` |
| `FromDate` | _(default, not pinned)_ | `new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)` |
| `ToDate` | _(default, not pinned)_ | `new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc)` |

### FR-1 snapshot population (4 items)

Each built via the existing `MakeSnapshot` helper with `ordered = 0`.

| # | ProductCode | available | Severity (via SetupSequence) |
|---|---|---|---|
| 1 | `"C001"` | any | Critical |
| 2 | `"C002"` | any | Critical |
| 3 | `"O001"` | any | Optimal |
| 4 | `"O002"` | any | Optimal |

### FR-2 snapshot population (5 items)

All items: `ordered = 0`, so `EffectiveStock = available`.

| # | ProductCode | available | UnitPrice | lastPurchase | Severity |
|---|---|---|---|---|---|
| 1 | `"P001"` | `10m` | `5.00m` | non-null | Critical |
| 2 | `"P002"` | `20m` | `3.00m` | non-null | Low |
| 3 | `"P003"` | `30m` | `2.00m` | non-null | Optimal |
| 4 | `"P004"` | `15m` | `4.00m` | non-null | Overstocked |
| 5 | `"P005"` | `0m` | n/a | `null` | NotConfigured |

`TotalInventoryValue` = (10 × 5.00) + (20 × 3.00) + (30 × 2.00) + (15 × 4.00) + 0 = **50 + 60 + 60 + 60 + 0 = 230.00m**

### `StockAnalysisSummaryDto` — expected values for FR-2

| Field | Expected value |
|---|---|
| `TotalProducts` | `5` |
| `CriticalCount` | `1` |
| `LowStockCount` | `1` |
| `OptimalCount` | `1` |
| `OverstockedCount` | `1` |
| `NotConfiguredCount` | `1` |
| `TotalInventoryValue` | `230.00m` |
| `AnalysisPeriodStart` | `new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)` |
| `AnalysisPeriodEnd` | `new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc)` |

### FR-1 assertions

| Assertion | Expected |
|---|---|
| `response.Items.Count` | `2` |
| All `response.Items[*].Severity` | `StockSeverity.Critical` |
| `response.Summary.TotalProducts` | `4` |
| `response.Summary.OptimalCount` | `2` |

### Key types (existing, unchanged)

- `MaterialStockSnapshot` — source data; `LastPurchase` is `MaterialPurchaseSnapshot?`
- `MaterialPurchaseSnapshot` — needs `UnitPrice` (decimal) and `Date` set; `Amount` and `TotalPrice` can be `0m`
- `StockSeverity` enum — `Critical | Low | Optimal | Overstocked | NotConfigured`
- `StockStatusFilter` enum — `All | Critical | ...`
- `IStockSeverityCalculator.DetermineStockSeverity(double, double, double, bool, bool) : StockSeverity` — mocked via `SetupSequence`
