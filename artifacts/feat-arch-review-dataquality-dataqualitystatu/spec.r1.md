# Specification: Add Structured Error Logging to DataQualityStatusTile

## Summary
Inject `ILogger<DataQualityStatusTile>` into `DataQualityStatusTile` and replace the silent bare `catch` block in `LoadDataAsync` with structured error logging. Aligns the tile's error-handling behavior with its sibling `DqtYesterdayStatusTile` so failures become diagnosable and the DataQuality module presents a consistent pattern.

## Background
The DataQuality module exposes dashboard tiles that summarize data-health signals for operators. Each tile implements a `LoadDataAsync` method that returns a status payload consumed by the frontend. When `LoadDataAsync` throws — due to database errors, mapping failures, or unexpected nulls — tiles are expected to degrade gracefully by returning a payload with `status = "error"`.

`DqtYesterdayStatusTile` does this correctly: it accepts `ILogger<DqtYesterdayStatusTile>` via constructor injection and logs the exception at `Error` level before returning the degraded payload. `DataQualityStatusTile`, located in the same folder (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/`), does not. Its constructor takes no logger, and its catch block (lines 71–74) swallows every exception:

```csharp
catch
{
    return new { status = "error", data = (object?)null, drillDown = ... };
}
```

This creates two concrete problems:
1. **Observability gap.** Any thrown exception in this tile vanishes — no log line, no telemetry, no signal to alert on. Operators see "error" in the dashboard with zero diagnostic trail.
2. **Inconsistency within the same module.** Two tiles in the same folder handle the same failure mode in two different ways. Future maintainers may copy the wrong pattern, propagating the silent-failure behavior to new tiles.

This was filed by the daily arch-review routine on 2026-06-02.

## Functional Requirements

### FR-1: Inject ILogger into DataQualityStatusTile
The `DataQualityStatusTile` class must accept `ILogger<DataQualityStatusTile>` as a constructor parameter, stored in a private readonly field following the same convention used by `DqtYesterdayStatusTile`.

**Acceptance criteria:**
- `DataQualityStatusTile` constructor declares `ILogger<DataQualityStatusTile> logger` as a parameter.
- Logger is stored in a private readonly field (naming consistent with `DqtYesterdayStatusTile`, e.g. `_logger`).
- DI container resolves `DataQualityStatusTile` successfully at startup (no missing registration errors).
- Existing constructor parameters and their order remain unchanged where possible to minimize call-site churn; if reordering is unavoidable, all call sites are updated.

### FR-2: Log Exceptions in LoadDataAsync Catch Block
Replace the bare `catch` block in `LoadDataAsync` with `catch (Exception ex)` and log the exception at `Error` level before returning the existing degraded payload.

**Acceptance criteria:**
- The catch block captures the exception as `ex`.
- `_logger.LogError(ex, "Failed to load DataQuality status tile")` is invoked before the return statement.
- The returned payload (shape, status value, drillDown content) is unchanged — callers and the frontend see no behavioral difference on the success-or-error contract.
- No `throw;` is added — the tile must continue to degrade gracefully rather than propagate.

### FR-3: Preserve Existing Behavior
The change must be purely additive from the caller's perspective. The tile's public surface, return shape, and status semantics remain identical.

**Acceptance criteria:**
- No public method signatures change other than the constructor.
- The error-path return object retains the same fields and values it has today.
- Any existing tests covering `DataQualityStatusTile` continue to pass without modification (other than supplying a logger mock to the constructor where applicable).

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. The logger call only fires on the exception path, which is already an exceptional/rare branch. Steady-state behavior is unchanged.

### NFR-2: Security
- The log message must not leak sensitive data. The fixed message `"Failed to load DataQuality status tile"` plus the exception object (which `ILogger.LogError` serializes) is acceptable.
- Do not log raw user identifiers, connection strings, or other PII as structured properties in this call.

### NFR-3: Consistency
The implementation must match the conventions used by `DqtYesterdayStatusTile` for: field naming (`_logger`), constructor parameter ordering convention, log level (`Error`), and message phrasing style. Where a stylistic choice exists, prefer the sibling tile's choice.

### NFR-4: Testability
A unit test must be able to verify that `LogError` is invoked when `LoadDataAsync` encounters an exception, using a mocked `ILogger<DataQualityStatusTile>`.

## Data Model
No data model changes. This is a behavioral fix in a single application-layer class.

## API / Interface Design

### Changed file
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`

### Constructor change
Add `ILogger<DataQualityStatusTile> logger` to the parameter list and assign it to a private readonly `_logger` field. Other existing dependencies remain.

### LoadDataAsync change (lines ~71–74)
Replace:
```csharp
catch
{
    return new { status = "error", data = (object?)null, drillDown = ... };
}
```
With:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to load DataQuality status tile");
    return new { status = "error", data = (object?)null, drillDown = ... };
}
```

### DI registration
If `DataQualityStatusTile` is registered explicitly anywhere (e.g. `AddScoped<DataQualityStatusTile>()` or in a module registration), confirm it still resolves — `ILogger<T>` is provided by the default .NET logging container and requires no additional registration.

### Public API / HTTP contract
No change. Endpoints consuming this tile see identical request/response shapes.

## Dependencies
- `Microsoft.Extensions.Logging.Abstractions` — already a transitive dependency throughout the backend; no new package references required.
- The sibling tile `DqtYesterdayStatusTile` serves as the reference implementation for the desired pattern.

## Out of Scope
- Refactoring or restructuring `DataQualityStatusTile` beyond the logging fix.
- Changing the response shape returned by `LoadDataAsync` on success or error.
- Auditing every other dashboard tile in the codebase for the same anti-pattern (may be a follow-up, but is not part of this change).
- Adding metrics, traces, or alerting beyond the `LogError` call.
- Changing the error-handling strategy (e.g. propagating exceptions instead of swallowing them).
- Modifying `DqtYesterdayStatusTile`.
- Adding new tests for unrelated tile behavior; only the new logging behavior needs test coverage.

## Open Questions
None.

## Status: COMPLETE