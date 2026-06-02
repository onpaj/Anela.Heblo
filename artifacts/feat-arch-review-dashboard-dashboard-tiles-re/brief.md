## Module
Dashboard

## Finding
Three dashboard tiles hardcode frontend routing paths directly in their `LoadDataAsync` return values:

**`DataQualityStatusTile.cs`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`, lines 37, 50):
```csharp
drillDown = new { href = "/data-quality", enabled = true }
```

**`DqtYesterdayStatusTile.cs`** (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`, lines 9, 53, 79, 93):
```csharp
private const string DrillDownHref = "/automation/data-quality";
...
drillDown = new { href = DrillDownHref, enabled = true }
```

**`FailedJobsTile.cs`** (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`, lines 9, 45):
```csharp
private const string FailedJobsUrl = "/hangfire/jobs/failed";
...
drillDown = new { url = FailedJobsUrl, enabled = true }
```

Note: the two DataQuality tiles are also inconsistent with each other — one uses `/data-quality` and the other uses `/automation/data-quality` for what appears to be the same page.

`development_guidelines.md` lists this as an explicit **forbidden practice**: _"Backend constructing frontend URLs: Violates separation of concerns, couples backend to frontend routing."_ The guidelines require the backend to return semantic data (e.g. filter parameters, route keys) and leave URL construction to the frontend.

## Why it matters
- Frontend routing changes (e.g. restructuring the `/automation/` segment) require backend changes in these tile files rather than a single frontend update.
- The inconsistency between `/data-quality` and `/automation/data-quality` across the two DataQuality tiles suggests these paths are already drifting.
- Violates the documented principle of frontend ownership over routing.

## Suggested fix
Replace hardcoded path strings with a semantic `routeKey` (or equivalent) that the frontend maps to an actual path:

```csharp
// Backend returns semantic intent:
drillDown = new { routeKey = "dataQuality", enabled = true }

// Frontend maps routeKey → actual path in one place
const drillDownRoutes: Record<string, string> = {
  dataQuality: "/automation/data-quality",
  hangfireFailedJobs: "/hangfire/jobs/failed",
};
```

This keeps routing entirely in the frontend and prevents backend–frontend coupling.

---
_Filed by daily arch-review routine on 2026-05-28._