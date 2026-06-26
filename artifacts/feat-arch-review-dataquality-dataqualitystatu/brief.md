## Module
DataQuality

## Finding
`DataQualityStatusTile.LoadDataAsync` (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`, lines 71–74) catches all exceptions with a bare `catch` block and no logging:

```csharp
catch
{
    return new { status = "error", data = (object?)null, drillDown = ... };
}
```

The class has no `ILogger` field at all. By contrast, its sibling tile `DqtYesterdayStatusTile` (same folder) correctly injects `ILogger<DqtYesterdayStatusTile>` and logs at `Error` level before returning the degraded response.

## Why it matters
- **Observability gap**: any database exception, mapping failure, or unexpected null that hits this code path disappears silently. There is no log entry, no metric, nothing to alert on. The dashboard just shows "error" with no diagnostic trail.
- **Inconsistency**: the two companion tiles in the same module handle errors differently, making the codebase harder to reason about and increasing the chance that future maintainers copy the wrong pattern.

## Suggested fix
Inject `ILogger<DataQualityStatusTile>` in the constructor (matching the pattern in `DqtYesterdayStatusTile`) and add a `LogError` call in the catch block:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to load DataQuality status tile");
    return new { status = "error", data = (object?)null, drillDown = ... };
}
```

---
_Filed by daily arch-review routine on 2026-06-02._