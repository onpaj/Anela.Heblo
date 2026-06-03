All tasks complete. Here is the output summary:

---

# Implementation: Add Structured Error Logging to DataQualityStatusTile

## What was implemented

Injected `ILogger<DataQualityStatusTile>` into `DataQualityStatusTile` via constructor (appended last, matching sibling convention) and replaced the silent bare `catch` block in `LoadDataAsync` with `catch (Exception ex)` + `_logger.LogError(ex, "Failed to load DataQuality status tile for {TestType}", DqtTestType.IssuedInvoiceComparison)`. The returned degraded payload shape is unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — added `using Microsoft.Extensions.Logging;`, `private readonly ILogger<DataQualityStatusTile> _logger;` field, updated constructor to accept `logger` as second parameter, replaced bare `catch` with `catch (Exception ex)` + structured `LogError` call
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` — added logger usings, added `Mock<ILogger<DataQualityStatusTile>> _loggerMock` field, updated constructor call to supply `_loggerMock.Object`, added new `LoadDataAsync_RepositoryThrows_LogsErrorOnce` test verifying `LogError` is called exactly once with the thrown exception

## Tests

- `DataQualityStatusTileTests.cs` — 4 tests total: 3 pre-existing (no-run, success, error-return-shape) + 1 new (`LoadDataAsync_RepositoryThrows_LogsErrorOnce` using Moq `Verify` on the underlying `ILogger.Log` method at `LogLevel.Error` with `Times.Once`)
- Full DataQuality test slice: 52/52 pass

## How to verify

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DataQualityStatusTileTests"
# Expected: 4/4 pass

dotnet build Anela.Heblo.sln
# Expected: 0 errors, 0 warnings
```

## Notes

- `DataQualityModule.cs` was intentionally not modified — `RegisterTile<DataQualityStatusTile>()` resolves the new `ILogger<T>` parameter automatically through the default .NET DI container.
- The three pre-existing tests use `_loggerMock.Object` (Moq mock) instead of `NullLogger<T>.Instance` as they now share the same fixture; this is functionally equivalent since the mock is a no-op for non-exception code paths.
- Log message uses `{TestType}` structured property (arch-review Amendment 1) with `DqtTestType.IssuedInvoiceComparison`, matching the sibling tile's structured-property style.

## PR Summary

Added structured error logging to `DataQualityStatusTile.LoadDataAsync` to close the observability gap flagged by the daily arch-review routine. The tile previously swallowed every exception silently; operators saw "error" in the dashboard with no diagnostic trail. The fix injects `ILogger<DataQualityStatusTile>` via constructor and replaces the bare `catch` with `catch (Exception ex)` + `_logger.LogError(...)` using the same pattern and conventions as the sibling `DqtYesterdayStatusTile`. Public surface and response shape are unchanged.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — added `_logger` field, updated constructor (logger last), replaced bare catch with structured LogError
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` — updated fixture to supply logger mock, added `LoadDataAsync_RepositoryThrows_LogsErrorOnce` Moq-verified test

## Status
DONE