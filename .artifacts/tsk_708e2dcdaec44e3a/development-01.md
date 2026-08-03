# Development: inject `TimeProvider` into the four recurring DQT jobs

Implemented exactly per `design-01.md`. No deviations.

## Files changed

**Source (4 files)** — `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/`:

- `InvoiceDqtJob.cs` — added `TimeProvider timeProvider` constructor param (before `logger`) + `_timeProvider` field; `DateTime.Today.AddDays(-1)` → `_timeProvider.GetUtcNow().DateTime).AddDays(-1)`.
- `StockWriteBackDqtJob.cs` — same shape, same substitution.
- `ProductPairingDqtJob.cs` — same shape; `DateTime.Today` → `_timeProvider.GetUtcNow().DateTime`.
- `LotStockReconciliationDqtJob.cs` — same shape; `DateTime.UtcNow` → `_timeProvider.GetUtcNow().DateTime` (fixes UTC-vs-local inconsistency with the other three jobs, not just the local-time issue).

No DI registration change: `services.AddSingleton(TimeProvider.System)` at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` already covers the new constructor parameter (confirmed by reading the file — same registration `DqtYesterdayStatusTile` already resolves through).

**Tests (4 files)** — `backend/test/Anela.Heblo.Tests/Features/DataQuality/`:

- `InvoiceDqtJobTests.cs`, `StockWriteBackDqtJobTests.cs`, `ProductPairingDqtJobTests.cs`, `LotStockReconciliationDqtJobTests.cs` — each gets:
  - `private readonly Mock<TimeProvider> _timeProviderMock = new();`, wired into the constructor call and given `.Setup(x => x.GetUtcNow()).Returns(FixedNow)` (pattern copied from `DqtYesterdayStatusTileTests.cs`).
  - `FixedNow = 2026-08-02T00:30:00Z` (UTC-midnight-straddling instant, so a wall-clock implementation running under `TZ=Europe/Prague` would resolve a different calendar date than the UTC-based fix — makes the test a real regression guard).
  - A new `ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock` fact asserting the persisted `DqtRun.DateFrom`/`DateTo` equal the expected date derived from `FixedNow`:
    - `InvoiceDqtJob`, `StockWriteBackDqtJob` (yesterday semantics) → `2026-08-01`
    - `ProductPairingDqtJob`, `LotStockReconciliationDqtJob` (today semantics) → `2026-08-02`

No other lines touched — `Metadata` blocks, `[AutomaticRetry]`, logging calls, and the enabled-check/persist/run sequence are unchanged in all four job files.

## Verification

- `dotnet build` (Application project): succeeded, 0 errors (pre-existing warnings only, none introduced by this change).
- `dotnet test --filter "FullyQualifiedName~DataQuality"`: **115/115 passed**, including the 4 new `ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock` facts.
- `dotnet format --verify-no-changes` scoped to the 8 changed files: clean, no formatting diffs.

## How to verify

```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQuality"
```

or target the four job test classes individually:

```
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceDqtJobTests|FullyQualifiedName~StockWriteBackDqtJobTests|FullyQualifiedName~ProductPairingDqtJobTests|FullyQualifiedName~LotStockReconciliationDqtJobTests"
```
