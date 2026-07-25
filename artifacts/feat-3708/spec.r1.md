# Specification: Unit test coverage for LowStockEfficiencyTile

## Summary
`LowStockEfficiencyTile.LoadDataAsync` has 0% line coverage today. This change adds a focused unit test suite that exercises the tile's business rule — counting materials with `StockEfficiencyPercentage < 20 && IsConfigured` — including its boundary condition and its error-response branch, using a mocked `IMediator`.

## Background
`LowStockEfficiencyTile` (`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs`) is a dashboard tile that reports how many purchase materials have critically low stock efficiency. It sends a `GetPurchaseStockAnalysisRequest` via `IMediator`, then filters the returned `Items` using a strict `<` comparison against 20% combined with an `IsConfigured` flag. This filter is a silent, easy-to-regress business rule (per the coverage-gap brief: if `IsConfigured` were accidentally dropped, all unconfigured materials sitting at 0% efficiency would inflate the counter and could trigger false alarms on the purchasing dashboard). No existing test exercises this file; the goal is a small, targeted test suite that locks in the current, correct behavior. This is a coverage-gap fix, not a functional change — no production code is expected to change.

## Functional Requirements

### FR-1: Test count of items matching both filter conditions
Add a unit test that mocks `IMediator.Send` to return a `GetPurchaseStockAnalysisResponse` (`Success = true`) containing a mix of `StockAnalysisItemDto` items with varying `StockEfficiencyPercentage` and `IsConfigured` values, and asserts that `LoadDataAsync`'s resulting `data.count` equals only the items where `StockEfficiencyPercentage < 20 AND IsConfigured == true`.

**Acceptance criteria:**
- Test data includes at least: an item below 20% and configured (counted), an item below 20% and NOT configured (excluded), and an item at/above 20% and configured (excluded).
- Assertion reads `count` from the tile's returned anonymous object (e.g. via `JsonSerializer.Serialize` + `JsonDocument.Parse`, matching the pattern used in `LowStockAlertTileTests.cs`, or via reflection) and confirms it matches only the items satisfying both conditions.
- `status` is asserted to be `"success"`.

### FR-2: Test the 20% boundary is exclusive
Add a unit test (or extend FR-1's test with a dedicated case) with an item at exactly `StockEfficiencyPercentage == 20` and `IsConfigured == true`, asserting it is excluded from `count` (strict `<`, not `<=`).

**Acceptance criteria:**
- An item with `StockEfficiencyPercentage = 20` and `IsConfigured = true` is present in the mocked response.
- The resulting `count` does not include this item (verified by using a set of items where this is the only one at the boundary, or by asserting an exact expected count that excludes it).

### FR-3: Test the error-response branch
Add a unit test that mocks `IMediator.Send` to return a `GetPurchaseStockAnalysisResponse` with `Success = false`, and asserts the tile returns the error shape `{ status = "error", error = "Failed to load stock analysis data" }` rather than attempting to count items.

**Acceptance criteria:**
- Mocked response has `Success = false` (e.g. via the `BaseResponse` error-code constructor, or by setting whatever property/pattern the codebase uses to make `Success` false — see Open Questions).
- Test asserts `status == "error"` and `error == "Failed to load stock analysis data"` on the returned object.
- No exception is thrown; the `!response.Success` branch is taken (not the `catch` branch).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are unit tests with mocked dependencies; no performance targets beyond normal fast unit-test execution (sub-second per test, consistent with the existing test suite in `backend/test/Anela.Heblo.Tests`).

### NFR-2: Security
Not applicable — no auth, no real data sources; `IMediator` is mocked with Moq, consistent with other dashboard tile tests (e.g. `LowStockAlertTileTests.cs`, `MaterialExpirationSummaryTileTests.cs`).

## Data Model
No data model changes. Tests construct in-memory instances of existing types:
- `GetPurchaseStockAnalysisResponse` (`Items: List<StockAnalysisItemDto>`, `Success` derived from `BaseResponse`/error code)
- `StockAnalysisItemDto` (relevant fields: `StockEfficiencyPercentage: double`, `IsConfigured: bool`; other required fields populated with simple defaults, e.g. `ProductCode`, `ProductName`)

## API / Interface Design
No API changes. New test file only:
- `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs`

Test class structure (mirroring the existing convention, e.g. `LowStockAlertTileTests`):
- Constructor sets up `Mock<IMediator>` and `Mock<TimeProvider>` (with a fixed `DateTimeOffset`), and instantiates `LowStockEfficiencyTile`.
- `_mediatorMock.Setup(x => x.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(...)` per test.
- Assertions via `FluentAssertions`, deserializing the returned anonymous object through `System.Text.Json` (consistent with existing dashboard tile tests) or via reflection helpers if simpler for anonymous types.

## Dependencies
- Existing test project `backend/test/Anela.Heblo.Tests` (xUnit, Moq, FluentAssertions — already referenced by sibling tile tests).
- No new NuGet packages.
- No changes to `LowStockEfficiencyTile.cs` or any other production file are required or expected.

## Out of Scope
- Any change to the tile's business logic, filter threshold, or response shape.
- Testing `GetPurchaseStockAnalysisHandler` or other upstream handlers/services (`IMaterialCatalogService`, `IStockSeverityCalculator`) — these are out of scope; `IMediator` is mocked at the tile boundary.
- Testing the `catch (Exception ex)` branch (mediator throwing) — not called out in the brief; may optionally be added but is not required.
- Testing `metadata`/`drillDown` field contents in detail — brief focuses specifically on the count filter and the error branch.
- Raising the file's coverage to 100% beyond what these 2-3 tests naturally provide.

## Open Questions
None.

## Status: COMPLETE
