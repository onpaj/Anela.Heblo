# Design: Unit test coverage for LowStockEfficiencyTile

## Component Design
No new or modified production components. `LowStockEfficiencyTile.LoadDataAsync`
(`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs`)
remains unchanged; this work adds one test component:

- **`LowStockEfficiencyTileTests`** (new)
  `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs`
  Namespace `Anela.Heblo.Tests.Features.Purchase.DashboardTiles`.
  Responsibility: exercise `LoadDataAsync` end-to-end through its public interface, with
  `IMediator` and `TimeProvider` mocked via Moq.
  - Constructor: builds `Mock<IMediator>`, `Mock<TimeProvider>` (fixed `DateTimeOffset`),
    instantiates `LowStockEfficiencyTile(mediator, timeProvider)`.
  - `[Fact] LoadDataAsync_WithMixedEfficiencyAndConfiguration_CountsOnlyLowEfficiencyConfiguredItems`
    — mocks a `Success = true` response with 4 `StockAnalysisItemDto` items (below-20%-configured,
    below-20%-unconfigured, exactly-20%-configured boundary, above-20%-configured); asserts
    `count == 1` and `status == "success"`. Covers FR-1 and FR-2.
  - `[Fact] LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus`
    — mocks a `Success = false` response via `new GetPurchaseStockAnalysisResponse(ErrorCodes.X)`;
    asserts `status == "error"` and `error == "Failed to load stock analysis data"`. Covers FR-3.

Assertions use `JsonSerializer.Serialize` + `JsonDocument.Parse` on the tile's returned anonymous
object, matching the existing `LowStockAlertTileTests` convention. No AutoFixture; plain object
initializers for DTOs.

## Data Schemas
No schema changes. Tests construct in-memory instances of existing types only:

- `GetPurchaseStockAnalysisResponse : BaseResponse`
  - `Items: List<StockAnalysisItemDto>`
  - `Success` — `true` via default constructor, `false` via `(ErrorCodes, Dictionary<string,string>?)` constructor
- `StockAnalysisItemDto`
  - `StockEfficiencyPercentage: double`
  - `IsConfigured: bool`
  - other fields (`ProductCode`, `ProductName`, etc.) left at their string.Empty defaults

Tile response shape under test (anonymous object, unchanged):
- Success: `{ status: "success", data: { count, date, ... }, metadata: {...} }`
- Error: `{ status: "error", error: "Failed to load stock analysis data" }`

No API, event, or persistence schema is introduced or modified by this change.
