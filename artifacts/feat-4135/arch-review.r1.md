# Architecture Review: Inject TimeProvider into GetPurchaseStockAnalysisHandler

## Skip Design: true

## Architectural Fit Assessment
This is a one-handler internal refactor with no new architectural surface. It brings `GetPurchaseStockAnalysisHandler` in line with a pattern already established and proven in the same Vertical Slice module: `CreatePurchaseOrderHandler` (`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs`) already injects `TimeProvider` as a constructor dependency and calls `_timeProvider.GetUtcNow()`. `TimeProvider.System` is already registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:135`, so the DI container already satisfies this dependency for every consumer — no composition-root change is needed. There is exactly one integration point: the constructor of `GetPurchaseStockAnalysisHandler`, resolved only via DI (MediatR) and via direct `new` in the test file. No API, contract, or persistence surface is touched.

## Proposed Architecture

### Component Overview
```
GetPurchaseStockAnalysisHandler (IRequestHandler<GetPurchaseStockAnalysisRequest, GetPurchaseStockAnalysisResponse>)
 ├── IMaterialCatalogService        (existing)
 ├── IStockSeverityCalculator       (existing)
 ├── IStockAnalysisCalculator       (existing)
 ├── ILogger<GetPurchaseStockAnalysisHandler> (existing)
 └── TimeProvider                   (NEW — resolved from existing DI singleton, same as CreatePurchaseOrderHandler)
```
No new components, services, or interfaces are introduced. The change is confined to one constructor parameter and two call sites inside `Handle`.

### Key Design Decisions

#### Decision 1: Mirror `CreatePurchaseOrderHandler`'s TimeProvider usage exactly
**Options considered:**
- (a) Inject `TimeProvider` via constructor, call `GetUtcNow().UtcDateTime` once and reuse the value for both fallbacks (matches `CreatePurchaseOrderHandler`).
- (b) Introduce a shared `IClock`/`ICurrentTimeService` abstraction wrapping `TimeProvider`.
- (c) Leave as-is and only fix testability via a settable static clock.

**Chosen approach:** (a).

**Rationale:** `TimeProvider` is the .NET 8 built-in abstraction, already registered in DI, and already the established convention in this exact module. Introducing a wrapper (b) would create a second, competing abstraction for the same concern in the same module — the opposite of what this fix is trying to achieve. Option (c) doesn't fix testability. This is a 1:1 mirror of proven, already-tested code; no new decision is actually being made.

#### Decision 2: Compute `now` once, apply to both fallbacks
**Options considered:** call `_timeProvider.GetUtcNow()` twice (once per fallback) vs. once, stored in a local `now` variable.

**Chosen approach:** Once, stored in `var now = _timeProvider.GetUtcNow().UtcDateTime;`, used for both `fromDate` and `toDate` fallbacks.

**Rationale:** Matches the spec's acceptance criteria exactly (FR-2) and avoids a same-request race where `FromDate` and `ToDate` could theoretically derive from different instants if `GetUtcNow()` were called twice (irrelevant for `TimeProvider.System` in practice, but it's free correctness and keeps the code deterministic for tests).

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Two files change:
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`

### Interfaces and Contracts
Constructor signature change only (append `TimeProvider timeProvider` as the **last** parameter, matching the placement convention used in `CreatePurchaseOrderHandler`):

```csharp
private readonly TimeProvider _timeProvider;

public GetPurchaseStockAnalysisHandler(
    IMaterialCatalogService materialCatalog,
    IStockSeverityCalculator stockSeverityCalculator,
    IStockAnalysisCalculator stockAnalysisCalculator,
    ILogger<GetPurchaseStockAnalysisHandler> logger,
    TimeProvider timeProvider)
{
    _materialCatalog = materialCatalog;
    _stockSeverityCalculator = stockSeverityCalculator;
    _stockAnalysisCalculator = stockAnalysisCalculator;
    _logger = logger;
    _timeProvider = timeProvider;
}
```

In `Handle`, replace lines 33–34:

```csharp
var now = _timeProvider.GetUtcNow().UtcDateTime;
var fromDate = request.FromDate ?? now.AddYears(-1);
var toDate = request.ToDate ?? now;
```

No changes to `GetPurchaseStockAnalysisRequest`, `GetPurchaseStockAnalysisResponse`, or any DTO. No public/MediatR contract changes. No DI registration changes — `TimeProvider.System` is already registered as a singleton in `ServiceCollectionExtensions.cs` and will be resolved automatically.

Test changes, mirroring `CreatePurchaseOrderHandlerTests` exactly:

```csharp
private readonly Mock<TimeProvider> _timeProviderMock;
private static readonly DateTimeOffset FixedNow = new(2024, 8, 2, 14, 30, 22, TimeSpan.Zero);
```

In the constructor:
```csharp
_timeProviderMock = new Mock<TimeProvider>();
_timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

_handler = new GetPurchaseStockAnalysisHandler(
    _materialCatalogMock.Object,
    _stockSeverityCalculatorMock.Object,
    new StockAnalysisCalculator(),
    _loggerMock.Object,
    _timeProviderMock.Object);
```
This single shared-constructor edit fixes every existing test's construction call in one place (`GetPurchaseStockAnalysisHandlerTests` has one `_handler` field built once in the class constructor — no per-test `new GetPurchaseStockAnalysisHandler(...)` call sites to hunt down).

Add one new test for the default-fallback path, e.g.:
```csharp
[Fact]
public async Task Handle_NullDates_DefaultsToOneYearWindowFromTimeProvider()
{
    var snapshots = CreateTestSnapshots();
    _materialCatalogMock
        .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(snapshots);
    _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
        It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
        .Returns(StockSeverity.Optimal);

    var request = new GetPurchaseStockAnalysisRequest { PageNumber = 1, PageSize = 10 };

    var response = await _handler.Handle(request, CancellationToken.None);

    response.Summary.AnalysisPeriodStart.Should().Be(FixedNow.UtcDateTime.AddYears(-1));
    response.Summary.AnalysisPeriodEnd.Should().Be(FixedNow.UtcDateTime);
}
```
Note: several existing tests (e.g. `Handle_ValidRequest_ReturnsAnalysisResponse`, `Handle_InvalidDateRange_ReturnsError`) currently construct `DateTime.UtcNow`-based requests directly — these are unaffected by this change since they pass explicit `FromDate`/`ToDate` and don't assert on the default-fallback branch; leave them as-is per the spec's "no behavior change for explicit dates" and "don't touch unrelated tests" scope.

### Data Flow
Unchanged. The only difference is where `now` comes from: previously `DateTime.UtcNow` (real wall clock, two separate reads), now `_timeProvider.GetUtcNow().UtcDateTime` (one read, injected and mockable). Everything downstream — snapshot fetch, filtering, sorting, pagination, summary calculation — consumes `fromDate`/`toDate` exactly as before.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing a test construction call site for the handler, causing a compile error | Low | Confirmed via file read: `GetPurchaseStockAnalysisHandlerTests` builds `_handler` exactly once in its class constructor — a single edit point, no scan needed. |
| Two separate `GetUtcNow()` calls could (in principle) return slightly different instants | Negligible | FR-2 already specifies capturing `now` once and reusing it for both fallbacks; implement it that way. |

No other risks — this is a mechanical, behavior-preserving change with an existing, tested precedent in the same module.

## Specification Amendments
None. The spec's acceptance criteria (FR-1–FR-3) match the codebase exactly as read: the current constructor has 4 parameters in the stated order, the current `Handle` has the exact two `DateTime.UtcNow` lines at 33–34 as quoted, and `GetPurchaseStockAnalysisHandlerTests` builds the handler once in its constructor (not per-test), making the "single call-site" assumption in FR-3 correct. Implement as specified.

One clarification for the developer, not a spec change: place the new `TimeProvider timeProvider` parameter **last** in the constructor (after `logger`), matching where `CreatePurchaseOrderHandler` places it relative to its other dependencies — this keeps the two handlers visually consistent for anyone comparing them side by side.

## Prerequisites
None. `TimeProvider.System` is already registered as a DI singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:135`. No migrations, config, or infrastructure changes are required before implementation can start.
