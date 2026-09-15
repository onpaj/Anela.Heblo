# Design: CriticalGiftPackagesTile error-shape unit test coverage

## Component Design

### `CriticalGiftPackagesTileTests` (new test class)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Logistics.DashboardTiles` (mirrors the folder path, matching `Anela.Heblo.Tests.Features.Purchase.DashboardTiles` for `LowStockEfficiencyTileTests`).
- **Responsibility:** Exercise the two currently-untested branches of `CriticalGiftPackagesTile.LoadDataAsync` — the `!response.Success` early-return and the outer `catch (Exception)` — and pin down their JSON response shape.
- **Collaborators (mocked):**
  - `Mock<IMediator>` — stubs `Send(GetAvailableGiftPackagesRequest, CancellationToken)` per test, either returning an unsuccessful `GetAvailableGiftPackagesResponse` or throwing.
  - `Mock<TimeProvider>` — constructed but not required to have `GetUtcNow()` stubbed for these two tests, since neither error branch reads `_timeProvider` (only the success branch does). Construct a plain `Mock<TimeProvider>()` without a `.Setup`, consistent with satisfying the constructor signature only.
- **System under test:** `CriticalGiftPackagesTile` (unchanged), instantiated once in the test class constructor with the two mocks above, reused across both `[Fact]` methods (xUnit gives each `[Fact]` a fresh class instance, so no shared mutable state risk).

### Test methods
1. `LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus`
   - Arrange: mock `IMediator.Send` to return a `GetAvailableGiftPackagesResponse` constructed via its inherited `BaseResponse` error-code constructor (`Success == false`).
   - Act: `await _tile.LoadDataAsync()`.
   - Assert (via `JsonSerializer.SerializeToElement`/`JsonDocument`):
     - `status` == `"error"`
     - `error` == `"Failed to load gift packages data"`
     - no `data` property present (shape-parity assertion, folded in per arch-review's Specification Amendment — not a separate test).
2. `LoadDataAsync_WhenServiceThrows_ReturnsErrorStatus`
   - Arrange: mock `IMediator.Send` to throw a distinctive exception, e.g. `ThrowsAsync(new InvalidOperationException("Simulated failure"))`.
   - Act: `await _tile.LoadDataAsync()` (must not throw — the tile's own `try/catch` swallows it).
   - Assert:
     - `status` == `"error"`
     - `error` == `"Simulated failure"` (i.e. `ex.Message`, proving this branch surfaces the real exception message rather than a fixed string)
     - no `data` property present (same shape-parity assertion as test 1).

No other components, files, or interfaces are touched. No `ITile`, `IMediator`, or contract changes.

## Data Schemas
No schema changes — this task adds test coverage only. For reference, the (unchanged) shapes under test:

**Success shape** (not modified by, and out of scope for, this task):
```json
{
  "status": "success",
  "data": { "count": 0, "date": "..." },
  "metadata": { "lastUpdated": "...", "source": "GiftPackageManufacture" },
  "drillDown": { "filters": { "severity": "Critical" }, "enabled": true, "tooltip": "..." }
}
```

**Error shape — `!response.Success` branch** (asserted by new test 1):
```json
{ "status": "error", "error": "Failed to load gift packages data" }
```

**Error shape — `catch (Exception)` branch** (asserted by new test 2):
```json
{ "status": "error", "error": "<ex.Message>" }
```

Both error shapes share identical property names (`status`, `error`) — this structural equivalence, despite the differing message-content source (fixed string vs. `ex.Message`), is exactly the contract the issue asks to be locked in by tests.
