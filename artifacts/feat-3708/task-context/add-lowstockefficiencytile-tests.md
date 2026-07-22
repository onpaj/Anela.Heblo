### task: add-lowstockefficiencytile-tests
**Goal:** Add unit tests for `LowStockEfficiencyTile` (`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs`) that lock in its filter rule (`StockEfficiencyPercentage < 20 && IsConfigured`) and error handling, per spec FR-1 (count of items matching both filter conditions), FR-2 (20% boundary is exclusive), and FR-3 (error-response branch). `IMediator` is mocked via Moq; no production code is expected to change.

**Files:**
- backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs (new)

**Steps:**
1. Create the new directory/file `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs` with namespace `Anela.Heblo.Tests.Features.Purchase.DashboardTiles` (this directory does not exist yet under `test/`; mirrors the production namespace path, matching the module-mirroring convention used elsewhere in the test project — not the Catalog-based location of `LowStockAlertTileTests`/`MaterialExpirationSummaryTileTests`, which are only style references).
2. In the constructor, set up `Mock<IMediator>` and `Mock<TimeProvider>` (with `_timeProviderMock.Setup(x => x.GetUtcNow()).Returns(fixedDateTimeOffset)` using a fixed `DateTime`, e.g. `new DateTime(2025, 10, 20, 10, 0, 0, DateTimeKind.Utc)`), and instantiate `new LowStockEfficiencyTile(_mediatorMock.Object, _timeProviderMock.Object)`.
3. Add `[Fact] LoadDataAsync_WithMixedEfficiencyAndConfiguration_CountsOnlyLowEfficiencyConfiguredItems` (covers FR-1 and FR-2):
   - Build a `GetPurchaseStockAnalysisResponse` (default constructor, `Success = true`) with `Items` containing at least 4 `StockAnalysisItemDto` entries built with plain object initializers (only `StockEfficiencyPercentage` and `IsConfigured` need meaningful values; other fields left at their `string.Empty`/default values):
     - Item A: `StockEfficiencyPercentage = 10`, `IsConfigured = true` → counted.
     - Item B: `StockEfficiencyPercentage = 10`, `IsConfigured = false` → excluded (fails `IsConfigured`).
     - Item C: `StockEfficiencyPercentage = 20`, `IsConfigured = true` → excluded (boundary, strict `<`, not `<=`; this is the FR-2 case).
     - Item D: `StockEfficiencyPercentage = 25`, `IsConfigured = true` → excluded (at/above threshold).
   - `_mediatorMock.Setup(x => x.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(response)`.
   - Act: `var result = await _tile.LoadDataAsync();`
   - Assert via `JsonSerializer.Serialize(result)` + `JsonDocument.Parse(json)` (matching `LowStockAlertTileTests`'s convention):
     - `doc.RootElement.GetProperty("status").GetString().Should().Be("success")`.
     - `doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(1)` — only Item A satisfies both conditions.
4. Add `[Fact] LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus` (covers FR-3):
   - Build `var response = new GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange);` (or any existing `ErrorCodes` member — the tile only checks `response.Success`, not the code) so `Success` is `false` via the `BaseResponse` error-code constructor.
   - `_mediatorMock.Setup(x => x.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(response)`.
   - Act: `var result = await _tile.LoadDataAsync();`
   - Assert via the same JSON-parse pattern:
     - `doc.RootElement.GetProperty("status").GetString().Should().Be("error")`.
     - `doc.RootElement.GetProperty("error").GetString().Should().Be("Failed to load stock analysis data")`.
   - Confirm no exception propagates from the test (i.e. the `!response.Success` branch is exercised, not the `catch` branch) — this is implicit in the test completing and asserting the error shape rather than throwing.
5. Build and run the new test file in isolation to confirm both tests pass and no other tests are affected.

**Acceptance criteria:**
- FR-1: A test with items below 20%+configured (counted), below 20%+not configured (excluded), and at/above 20%+configured (excluded) asserts `data.count` equals exactly the count of items satisfying both `StockEfficiencyPercentage < 20` and `IsConfigured == true`, and `status == "success"`.
- FR-2: An item with `StockEfficiencyPercentage == 20` and `IsConfigured == true` is present in the mocked data and is excluded from `count` (strict `<`, not `<=`), verified via an exact expected count.
- FR-3: A mocked response with `Success = false` produces `status == "error"` and `error == "Failed to load stock analysis data"`, with no exception thrown (the `!response.Success` branch, not `catch`, is exercised).
- No changes to `LowStockEfficiencyTile.cs` or any other production file.
- New test file compiles and both `[Fact]` tests pass.

**Verification:**
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~LowStockEfficiencyTileTests` (or the equivalent project test command) passes.
