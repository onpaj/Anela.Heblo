## Module
DataQuality

## Finding
The two DataQuality dashboard tiles hard-code different frontend routes in their `drillDown.href` payloads:

- **`DataQualityStatusTile`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`, line 36):
  ```csharp
  drillDown = new { href = "/data-quality", enabled = true }
  ```

- **`DqtYesterdayStatusTile`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`, line 9):
  ```csharp
  private const string DrillDownHref = "/automation/data-quality";
  ```

Both tiles are in the same module and are about the same DataQuality feature, yet they point to two different paths. At least one is wrong.

This is also an instance of the forbidden practice listed in `docs/architecture/development_guidelines.md`:
> **Backend constructing frontend URLs** — Violates separation of concerns, couples backend to frontend routing

## Why it matters
- **Correctness**: clicking the drill-down on one of the two tiles leads to a wrong or non-existent route.
- **Coupling**: any future frontend route rename requires a backend code change. The issue was likely introduced by exactly that kind of rename — the inconsistency is the natural consequence of this pattern.
- **Discoverability**: a developer looking at either tile in isolation would not notice the contradiction.

## Suggested fix
Short-term (fix the inconsistency): decide which route is correct, unify both tiles to use that single value via a shared constant, e.g. in a `DataQualityConstants.cs`:
```csharp
public static class DataQualityConstants
{
    public const string DrillDownRoute = "/automation/data-quality";
}
```

Long-term (correct fix per the architecture): the backend should return a semantic key (e.g. `"drillDownPage": "data-quality"`) and the frontend should resolve the actual URL. This removes the coupling entirely.

---
_Filed by daily arch-review routine on 2026-06-01._