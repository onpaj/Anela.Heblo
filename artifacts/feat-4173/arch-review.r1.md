# Architecture Review: CriticalGiftPackagesTile error-shape unit test coverage

## Skip Design: true
This is a backend-only, test-only change (no new/changed UI components, no API surface change, no production code change). Per the designer's own routing rule, backend-only features with no UI skip the UX/UI sections; there is no component-boundary or data-schema decision left to make beyond what this review already pins down, so the design phase should emit the brief "no UI" backend-only design format.

## Architectural Fit Assessment
This fits cleanly as a pure test-suite addition inside the `Logistics` module's existing test project structure. `CriticalGiftPackagesTile` is one of several `ITile` implementations under `Anela.Heblo.Application/Features/Logistics/DashboardTiles/` (siblings: `ErrorBoxesTile`, `InTransitBoxesTile`, `ReceivedBoxesTile`, `TransportBoxBaseTile`). None of those currently have a corresponding test folder either — `backend/test/Anela.Heblo.Tests/Features/Logistics/` exists but has no `DashboardTiles/` subfolder yet, unlike `Purchase`, `WeatherForecast`, `Manufacture`, `Catalog`, `BackgroundJobs`, `DataQuality`, and `Packaging`, which all already have `Features/{Module}/DashboardTiles/{Tile}Tests.cs`. Adding `Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs` brings Logistics in line with that established, module-per-folder convention — no new pattern is introduced.

There is no module-boundary concern: the test only exercises `CriticalGiftPackagesTile`'s public `LoadDataAsync` via mocked `IMediator`/`TimeProvider`, exactly as `LowStockEfficiencyTileTests` (Purchase module) and `WeatherForecastTileTests` do for their own tiles. No cross-module coupling, no shared test fixtures, no new abstractions.

## Proposed Architecture

### Component Overview
```
CriticalGiftPackagesTile (existing, unchanged)
        │  implements ITile.LoadDataAsync
        │  depends on: IMediator, TimeProvider
        ▼
[NEW] CriticalGiftPackagesTileTests (xUnit)
        │  mocks: Mock<IMediator>, Mock<TimeProvider>
        │  drives: two [Fact] tests, one per untested branch
        ▼
JsonSerializer / JsonDocument assertions on the anonymous `object` result
        (no typed response DTO exists for tile payloads — matches existing pattern)
```

### Key Design Decisions

#### Decision 1: Test file location and naming
**Options considered:**
- (a) `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs` (new subfolder, module-per-folder convention)
- (b) Flat file directly under `Features/Logistics/` alongside `GiftPackageManufactureServiceTests.cs`
- (c) Under `Features/Logistics/GiftPackageManufacture/` since the tile consumes that use case's request/response

**Chosen approach:** (a).
**Rationale:** Every other module with dashboard tiles under test (`Purchase`, `WeatherForecast`, `Manufacture`, `Catalog`, `BackgroundJobs`, `DataQuality`, `Packaging`) uses `Features/{Module}/DashboardTiles/{Tile}Tests.cs`. `CriticalGiftPackagesTile.cs` itself lives at `Features/Logistics/DashboardTiles/CriticalGiftPackagesTile.cs` (mirroring the `Features/{Module}/DashboardTiles/` production-code convention too), so the test path should mirror the source path 1:1, as every existing sibling does. This is not a judgment call — it is following an unambiguous, universally-applied existing pattern, so no alternative should be substituted at implementation time.

#### Decision 2: Assertion mechanism (JSON traversal vs. typed deserialization)
**Options considered:**
- (a) `JsonSerializer.Serialize`/`SerializeToElement` + `JsonDocument`/`JsonElement.GetProperty(...)` traversal
- (b) Define a new typed `CriticalGiftPackagesTileErrorDto` and deserialize into it for strongly-typed assertions

**Chosen approach:** (a).
**Rationale:** `LoadDataAsync` returns bare anonymous `object` — there is no existing typed contract for tile payloads anywhere in the codebase (confirmed: `LowStockEfficiencyTileTests` and `WeatherForecastTileTests` both use raw `JsonSerializer`/`JsonDocument`, never a typed DTO). Introducing a typed DTO now would be net-new production code to support a test, which is out of scope for a coverage-only task and would diverge from every sibling test. Reuse the exact JSON-traversal idiom already proven in this codebase.

#### Decision 3: Exception type used in the catch-branch test
**Options considered:**
- (a) A generic `Exception`/`InvalidOperationException` with a distinctive message
- (b) A domain-specific exception type

**Chosen approach:** (a) — generic exception (e.g. `InvalidOperationException("Simulated failure")`), matching `WeatherForecastTileTests`'s use of a generic `HttpRequestException` for its equivalent throw-path test.
**Rationale:** The `catch (Exception ex)` block in `CriticalGiftPackagesTile.cs` is unconditional and type-agnostic — it returns `ex.Message` regardless of exception type. The test's job is to prove the catch-all contract, not any specific failure mode, so any distinctive `Exception` subtype is sufficient and keeps the test decoupled from unrelated exception hierarchies (e.g. don't invent a dependency on `IMediator`-specific exception types).

## Implementation Guidance

### Directory / Module Structure
Create exactly one new file:
- `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs`

No other files, folders, or `.csproj` changes are needed — `Anela.Heblo.Tests` already compiles everything under `Features/Logistics/`, so a new subfolder is picked up automatically (confirmed via the existing `Features/{Module}/DashboardTiles/` pattern across 7+ other modules using the same test project).

### Interfaces and Contracts
No new interfaces or contracts. The test consumes only what already exists and is public:
- `CriticalGiftPackagesTile` constructor: `(IMediator mediator, TimeProvider timeProvider)`
- `ITile.LoadDataAsync(Dictionary<string,string>? parameters = null, CancellationToken cancellationToken = default) : Task<object>`
- `GetAvailableGiftPackagesRequest` / `GetAvailableGiftPackagesResponse : BaseResponse` (for constructing the mocked `!Success` response — use the `BaseResponse` error-code constructor, mirroring `new GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange)` from `LowStockEfficiencyTileTests`; any valid `ErrorCodes` member is acceptable since the tile ignores the specific code and always returns the fixed string `"Failed to load gift packages data"`).

### Data Flow
Test 1 (`!Success` branch): `Mock<IMediator>.Setup(x => x.Send(It.IsAny<GetAvailableGiftPackagesRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(unsuccessfulResponse)` → `await tile.LoadDataAsync()` → serialize → assert `status == "error"`, `error == "Failed to load gift packages data"`, no `data` property.

Test 2 (`catch` branch): `Mock<IMediator>.Setup(...).ThrowsAsync(new InvalidOperationException("some message"))` → `await tile.LoadDataAsync()` → serialize → assert `status == "error"`, `error == "some message"`, no `data` property.

Both tests reuse a shared constructor-level `Mock<IMediator>` / `Mock<TimeProvider>` / `CriticalGiftPackagesTile` instance per the `LowStockEfficiencyTileTests` constructor-setup pattern (xUnit creates a fresh test class instance per `[Fact]`, so no cross-test mock leakage).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Asserting on the exact fixed string `"Failed to load gift packages data"` makes the test brittle to a future intentional message wording change | Low | Acceptable and intentional — this is exactly the "lock in the current contract" goal from the issue; a deliberate wording change should require touching the test, which is the correct signal |
| `GetAvailableGiftPackagesResponse`'s `BaseResponse` error-code constructor signature/`ErrorCodes` enum member names are unverified in this review (architect did not exhaustively read `BaseResponse`/`ErrorCodes`) | Low | Implementer should read `Anela.Heblo.Application/Shared/BaseResponse.cs` and the `ErrorCodes` enum (or copy the exact pattern already used in `LowStockEfficiencyTileTests.cs`, which constructs `GetPurchaseStockAnalysisResponse(ErrorCodes.InvalidDateRange)` the same way) before writing the test — do not guess the constructor shape |
| Scope creep: adding a success-path test or refactoring the tile's error shapes into a shared type | Low | Explicitly out of scope per spec.r1.md; implementer should add only the two tests (plus the optional FR-3 shape-parity assertions folded into those same two tests, not a third test) |

## Specification Amendments
None — spec.r1.md is implementable as written. One clarification for the planner: FR-3's shape-parity assertion should be inlined into the FR-1 and FR-2 test bodies (e.g. an additional assertion line each), not written as a separate third `[Fact]`, to avoid an artificial coupling between two independently-mocked scenarios.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The test project already has `Moq`, `FluentAssertions`, `System.Text.Json`, and `xUnit` available (confirmed via `LowStockEfficiencyTileTests.cs` and `WeatherForecastTileTests.cs` using all four already).
