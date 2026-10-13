# Specification: CriticalGiftPackagesTile error-shape unit test coverage

## Summary
`CriticalGiftPackagesTile.LoadDataAsync` has two untested branches: the `!response.Success` path and the outer `catch (Exception)` path. Both return an "error" status object, but with different shapes, and neither is asserted by any test today (0.0% line coverage). This spec scopes the addition of two focused unit tests that lock in the exact JSON shape of each error path, using the existing `LowStockEfficiencyTileTests` / `WeatherForecastTileTests` pattern already established in this codebase for other dashboard tiles.

## Background
`backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/CriticalGiftPackagesTile.cs` implements `ITile.LoadDataAsync`, which:
1. Sends `GetAvailableGiftPackagesRequest` via `IMediator`.
2. If `!response.Success`, returns `{ status: "error", error: "Failed to load gift packages data" }` (no `data`, no `count`).
3. Otherwise counts `GiftPackageSeverity.Critical` items and returns `{ status: "success", data: { count, date }, metadata: {...}, drillDown: {...} }`.
4. Any exception thrown during the above (including inside the `_mediator.Send` call) is caught by an outer `try/catch` and returns `{ status: "error", error: ex.Message }` — a shape that reuses the same `status`/`error` property names as the `!Success` branch but with a caller-controlled `error` message instead of the fixed string.

This tile was flagged by the weekly coverage-gap routine (CI run #34699120372) at 0.0% line coverage against a 60% threshold. Per the issue: dashboard consumers rely on the error-status shape to render fallback UI, and if the two error paths ever diverge in shape (e.g. one gains a `data` object with nulls, the other doesn't) a consumer could null-reference or show a wrong count. This is a pure test-coverage task — no production code in `CriticalGiftPackagesTile.cs` is expected to change.

## Functional Requirements

### FR-1: Unit test for the `!response.Success` branch
Add a test that mocks `IMediator.Send(GetAvailableGiftPackagesRequest, ...)` to return a `GetAvailableGiftPackagesResponse` with `Success == false` (i.e. constructed via its `BaseResponse` error-code constructor, matching the pattern used by `GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange)` in `LowStockEfficiencyTileTests`), then calls `LoadDataAsync()` and asserts the serialized result.

**Acceptance criteria:**
- Serializing the result (`JsonSerializer.Serialize`/`SerializeToElement`) and parsing it shows `status == "error"`.
- `error` equals exactly `"Failed to load gift packages data"`.
- The result has no `data` property (or, if asserted structurally, no populated `count`/`date` fields) — i.e. it does NOT resemble the success shape.
- `_mediator.Send` is invoked exactly once with a `GetAvailableGiftPackagesRequest` (loose match is sufficient; asserting `SalesCoefficient == 1.0m` is optional/nice-to-have, not required).

### FR-2: Unit test for the outer `catch (Exception)` branch
Add a test that mocks `IMediator.Send(...)` to throw (`ThrowsAsync(new InvalidOperationException("..."))` or similar, matching the `WeatherForecastTileTests.LoadDataAsync_WhenClientThrows_ReturnsErrorStatus` pattern), then calls `LoadDataAsync()` and asserts the serialized result.

**Acceptance criteria:**
- The call does not throw — `LoadDataAsync` catches the exception and returns a value (the awaited `Task<object>` completes normally).
- Serializing the result shows `status == "error"`.
- `error` equals the thrown exception's `Message` (i.e. `ex.Message`, not a fixed string) — this is the key contract distinguishing this branch from FR-1's fixed-string branch, and is the assertion that would catch a future accidental shape merge/divergence between the two paths.

### FR-3 (optional, recommended): Cross-branch shape-parity assertion
To directly capture "why it matters" from the issue (consumers must treat both error shapes as equivalent), assert in both FR-1 and FR-2 tests that the result's JSON property set is limited to `status` and `error` (no stray `data`/`metadata`/`drillDown` keys leak from a partially-constructed success object). This can be done by asserting `doc.RootElement.EnumerateObject().Select(p => p.Name)` equals `{"status", "error"}`, or more simply by asserting `TryGetProperty("data", out _)` is `false` on both results.

**Acceptance criteria:**
- Both the FR-1 and FR-2 result shapes expose exactly the same top-level property names (`status`, `error`), confirming the two error paths are structurally identical to each other even though their `error` string content differs by design.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a synchronous-mock unit test, no I/O, expected to run in milliseconds as part of the existing `dotnet test` suite.

### NFR-2: Security
Not applicable — no new production code, no new external inputs, no credentials involved.

## Data Model
No new data model. Relevant existing types (unchanged):
- `GetAvailableGiftPackagesResponse : BaseResponse` (`Success`, error-code-driven), with `GiftPackages: List<GiftPackageDto>`.
- `GiftPackageDto.Severity: GiftPackageSeverity` enum (`Critical`, `Severe`, `Optimal`).
- Anonymous response shapes returned by `LoadDataAsync` (untyped `object`, asserted only via JSON serialization in tests, per the established pattern in this codebase — there is no strongly-typed tile-response DTO to construct directly).

## API / Interface Design
No API surface changes. Test-only addition:
- New test class `CriticalGiftPackagesTileTests` in a new file `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs` (new `DashboardTiles` subfolder under the existing `Features/Logistics/` test folder, matching the `Features/{Module}/DashboardTiles/{Tile}Tests.cs` convention used by `LowStockEfficiencyTileTests`, `WeatherForecastTileTests`, `ManualActionRequiredTileTests`, etc.).
- Constructor wires `Mock<IMediator>` and `Mock<TimeProvider>` (the tile's two constructor dependencies), instantiates `CriticalGiftPackagesTile`, following the exact `LowStockEfficiencyTileTests` constructor pattern (`_timeProviderMock.Setup(x => x.GetUtcNow()).Returns(fixedDateTime)` is optional for the two error-path tests since neither error branch reads `_timeProvider`, but should still be constructed since it's a required constructor argument).
- Uses `System.Text.Json.JsonSerializer` + `JsonDocument`/`JsonElement` for shape assertions (not a strongly-typed DTO), and `FluentAssertions` (`.Should().Be(...)`) for assertion style, and `Moq` for mocking — all per `docs/architecture/testing-strategy.md`'s mandated stack (xUnit, Moq, FluentAssertions) and the two reference test files read during this analysis.

## Dependencies
- Existing `Mock<IMediator>` / `Mock<TimeProvider>` mocking approach already proven in `LowStockEfficiencyTileTests.cs` and `WeatherForecastTileTests.cs` — no new test infrastructure, helpers, or fixtures required.
- No dependency on other in-flight features or modules.

## Out of Scope
- Any change to `CriticalGiftPackagesTile.cs` production code (the issue is coverage-only; the two branches already behave correctly per the issue's own description — the task is to assert/lock in that behavior, not alter it).
- Testing the success path (`response.Success == true` with populated `GiftPackages`) — already understood to be a separate, pre-existing coverage gap not called out by this issue; may already be untested too, but adding it is not required to close this issue and is left out to keep the change surgical. (Note to implementer: if a success-path test happens to already exist or is trivially added alongside for completeness, that is acceptable but not required — do not expand scope beyond what's needed to close the two branches named in the issue.)
- Any change to `GetAvailableGiftPackagesHandler`, `GetAvailableGiftPackagesResponse`, or any other module's dashboard tile.
- Any integration-level or E2E test — this is unit-test-only per the issue's "Suggested approach" and per `testing-strategy.md`'s guidance that MediatR-consuming components are unit-tested with mocks.
- Reconciling the two error shapes into one truly identical contract (e.g. introducing a shared error-result helper/DTO) — flagged as a future architectural improvement if desired, but not requested by this issue and would be a production-code change exceeding a "test coverage" task's scope.

## Open Questions
None.

## Status: COMPLETE
