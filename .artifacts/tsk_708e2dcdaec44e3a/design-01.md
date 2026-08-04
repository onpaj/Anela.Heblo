# Design: inject `TimeProvider` into the four recurring DQT jobs

No UI is involved — this is a server-side date-derivation fix confined to four job classes and their unit tests. UX/UI section omitted.

## 1. Component design

### 1.1 Boundary and responsibility (unchanged)

Each `IRecurringJob` implementation (`InvoiceDqtJob`, `StockWriteBackDqtJob`, `ProductPairingDqtJob`, `LotStockReconciliationDqtJob`) keeps its existing responsibility: check if enabled → compute the date window → create+persist a `DqtRun` → hand off to its runner (`IInvoiceDqtJobRunner` or `IDriftDqtJobRunner`). The only responsibility that changes is *where the current instant comes from*.

Reference implementation already in the module, to be copied exactly: `DqtYesterdayStatusTile` (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs:14,25-33,39`) — constructor-injects `TimeProvider`, stores as `_timeProvider`, and computes `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime)`.

### 1.2 Per-job change

Each job adds one constructor parameter, in the last-but-one position (before `ILogger`, matching the tile's ordering of `repository, timeProvider, logger`):

```csharp
public InvoiceDqtJob(
    IDqtRunRepository repository,
    IInvoiceDqtJobRunner jobRunner,
    IRecurringJobStatusChecker statusChecker,
    TimeProvider timeProvider,
    ILogger<InvoiceDqtJob> logger)
{
    _repository = repository;
    _jobRunner = jobRunner;
    _statusChecker = statusChecker;
    _timeProvider = timeProvider;
    _logger = logger;
}
```

Same shape for `StockWriteBackDqtJob`, `ProductPairingDqtJob`, `LotStockReconciliationDqtJob` (with each one's own runner type and logger category). Add `private readonly TimeProvider _timeProvider;` alongside the other fields.

Date-derivation line, per file — mechanical substitution, no other logic touched:

| File | Before | After |
|---|---|---|
| `InvoiceDqtJob.cs:44` | `DateOnly.FromDateTime(DateTime.Today.AddDays(-1))` | `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime).AddDays(-1)` |
| `StockWriteBackDqtJob.cs:44` | `DateOnly.FromDateTime(DateTime.Today.AddDays(-1))` | `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime).AddDays(-1)` |
| `ProductPairingDqtJob.cs:46` | `DateOnly.FromDateTime(DateTime.Today)` | `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime)` |
| `LotStockReconciliationDqtJob.cs:45` | `DateOnly.FromDateTime(DateTime.UtcNow)` | `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime)` |

`LotStockReconciliationDqtJob.cs:44`'s existing comment (`// Snapshot reconciliation — from/to are the current day for both bounds.`) stays as-is; it documents the from/to-equal-today business rule, not the clock source.

No other line in any of the four files changes. `Metadata` blocks, retry attributes (`ProductPairingDqtJob`'s `[AutomaticRetry]`), logging calls, and the enabled-check/persist/run sequence are untouched.

### 1.3 DI registration

None needed. `TimeProvider.System` is already registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` (`services.AddSingleton(TimeProvider.System);`) and is what `DqtYesterdayStatusTile` already resolves through constructor injection in production today. The four jobs pick up the same registration automatically — confirmed, not just assumed, since the tile already proves the wiring works.

## 2. Test design

### 2.1 Mocking pattern — reuse what's already in this test suite

`DqtYesterdayStatusTileTests.cs:14-28` (same module) establishes the pattern to reuse verbatim — `Moq`'s `Mock<TimeProvider>` with `.Setup(x => x.GetUtcNow()).Returns(fixedOffset)`. `TimeProvider.GetUtcNow()` is virtual, so `Mock<TimeProvider>` works without any extra test package. This resolves the plan's open question: **no new dependency** (`Microsoft.Extensions.TimeProvider.Testing` / `FakeTimeProvider`) is needed — the existing `Mock<TimeProvider>` approach is already proven in this exact module.

Each of the four existing test classes (`InvoiceDqtJobTests`, `StockWriteBackDqtJobTests`, `ProductPairingDqtJobTests`, `LotStockReconciliationDqtJobTests`) gets:

```csharp
private readonly Mock<TimeProvider> _timeProviderMock = new();
```

added alongside the other `Mock<...>` fields, wired into the constructor call at the same position as production code, and given a `.Setup(x => x.GetUtcNow()).Returns(FixedNow)` in the test constructor (matching `DqtYesterdayStatusTileTests`'s style of a `private static readonly DateTimeOffset FixedNow` field).

### 2.2 New/changed assertions, per job

Add one `[Fact]` per job asserting the `DqtRun` persisted via `AddAsync` carries the date(s) derived from the fixed clock — extending the existing `AddAsync`-argument assertion pattern already used in `ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner` (see `InvoiceDqtJobTests.cs:58-65`, `LotStockReconciliationDqtJobTests.cs:66-73`), which currently checks `TestType`/`TriggerType`/`Status` but not the date. Two options, pick per file based on what reads clean:

- extend the existing `AddAsync`-argument `It.Is<DqtRun>(...)` predicate to also check `run.DateFrom == expected && run.DateTo == expected`, or
- add a dedicated `ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock` fact with a UTC-midnight-straddling fixed instant.

Go with the dedicated-fact option for clarity and because it's the one that actually regression-guards the fix (a predicate silently matching `It.IsAny<DateOnly>()`-shaped defaults wouldn't fail against the old code the same way a targeted assertion does).

Per-job fixed instant and expected value (boundary case: instant just after UTC midnight, so a wall-clock-based implementation running in `TZ=Europe/Prague` — UTC+1/+2 — would resolve a different calendar date than a UTC-based one, making the test a real regression guard):

| Job | `FixedNow` (UTC) | Window semantics | Expected `DateFrom`/`DateTo` |
|---|---|---|---|
| `InvoiceDqtJob` | `2026-08-02T00:30:00Z` | yesterday | `2026-08-01` |
| `StockWriteBackDqtJob` | `2026-08-02T00:30:00Z` | yesterday | `2026-08-01` |
| `ProductPairingDqtJob` | `2026-08-02T00:30:00Z` | today | `2026-08-02` |
| `LotStockReconciliationDqtJob` | `2026-08-02T00:30:00Z` | today | `2026-08-02` |

Each fact:

```csharp
[Fact]
public async Task ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock()
{
    _statusCheckerMock
        .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>(), true))
        .ReturnsAsync(true);
    _repositoryMock
        .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((DqtRun run, CancellationToken _) => run);
    _repositoryMock
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(1);
    _jobRunnerMock
        .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    await _sut.ExecuteAsync(CancellationToken.None);

    _repositoryMock.Verify(
        r => r.AddAsync(
            It.Is<DqtRun>(run => run.DateFrom == ExpectedDate && run.DateTo == ExpectedDate),
            It.IsAny<CancellationToken>()),
        Times.Once);
}
```

`_timeProviderMock` is wired in the constructor (§2.1) so no per-test setup for it is needed — this fact only needs to add the missing collaborator setups already used by the sibling `ExecuteAsync_JobEnabled...` fact in the same file.

This fails against today's code for exactly the tests that matter: on a UTC test-runner it wouldn't fail (system clock and mocked `TimeProvider` would agree on the date most of the day), but it becomes a real assertion once `_timeProvider.GetUtcNow()` is actually threaded through in FR-2 — pre-FR-2 the class won't even compile with the new constructor parameter, so "fails on old code" is enforced structurally (constructor signature), not just behaviorally.

### 2.3 No test-file `LotStockReconciliationDqtJobTests.cs:44` comment change needed

`LotStockReconciliationDqtJob`'s test file has no existing date-related test to touch beyond the constructor and the new fact above.

## 3. Data / interfaces

No schema, request/response, or event-payload changes. `DqtRun.Start(testType, dateFrom, dateTo, triggerType)` (`backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtRun.cs:22`) is unchanged — only the `dateFrom`/`dateTo` values fed into it at the call site change source.

## 4. Out of scope (confirmed during design)

- `DqtYesterdayStatusTile` — untouched, used only as the reference pattern.
- DI registration in `ServiceCollectionExtensions.cs` — untouched; `services.AddSingleton(TimeProvider.System)` at line 130 already covers the new constructor parameters.
- `Microsoft.Extensions.TimeProvider.Testing` / `FakeTimeProvider` package — not needed; `Mock<TimeProvider>` (Moq) is the established in-repo pattern for this exact scenario.
- Runner internals (`IInvoiceDqtJobRunner`, `IDriftDqtJobRunner`), Hangfire config, cron expressions, retry attributes — all unaffected by this change.
