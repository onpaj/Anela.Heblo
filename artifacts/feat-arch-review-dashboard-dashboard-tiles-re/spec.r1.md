# Specification: Remove Hardcoded Frontend Routes from Dashboard Tiles

## Summary
Three backend dashboard tiles currently embed frontend route paths (`/data-quality`, `/automation/data-quality`, `/hangfire/jobs/failed`) directly in their drill-down payloads, violating the documented separation between backend and frontend ownership. This work replaces the hardcoded paths with a semantic `routeKey` token resolved to an actual path by a single frontend lookup, removing the coupling and the existing drift between the two DataQuality tiles.

## Background
`docs/architecture/development_guidelines.md` explicitly forbids the backend from constructing frontend URLs because it couples backend code to frontend routing. The arch-review routine flagged three tiles on 2026-05-28 that violate this rule:

- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` returns `drillDown.href = "/data-quality"`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` returns `drillDown.href = "/automation/data-quality"` via a `DrillDownHref` constant.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` returns `drillDown.url = "/hangfire/jobs/failed"` via a `FailedJobsUrl` constant.

Two of the three tiles claim to drill into the same Data Quality page yet disagree on the path, demonstrating the predicted drift. The payload shape is also inconsistent: two tiles use `href`, the third uses `url`. Both issues are symptoms of the same root cause — routing knowledge living in the wrong layer.

The fix is to have the backend return semantic intent (`routeKey`) and the frontend own a single mapping from `routeKey` → path.

## Functional Requirements

### FR-1: Backend tiles emit `routeKey` instead of hardcoded paths
Each of the three tiles must return a drill-down payload that identifies the destination by a semantic, frontend-agnostic key rather than a URL path.

**Acceptance criteria:**
- `DataQualityStatusTile.LoadDataAsync` returns `drillDown = { routeKey = "dataQuality", enabled = <bool> }` (no `href`, no `url`).
- `DqtYesterdayStatusTile.LoadDataAsync` returns `drillDown = { routeKey = "dataQuality", enabled = <bool> }` for **all four** return paths currently emitting `DrillDownHref` (lines 53, 79, 93, and any other).
- `FailedJobsTile.LoadDataAsync` returns `drillDown = { routeKey = "hangfireFailedJobs", enabled = <bool> }`.
- The `DrillDownHref` and `FailedJobsUrl` constants are removed from the respective files.
- No backend file under `backend/src/Anela.Heblo.Application/Features/**/DashboardTiles/**` contains a string literal that looks like a frontend route (regex `"/[a-z][a-z0-9/-]*"` with a leading slash). Existing non-tile occurrences are out of scope.

### FR-2: Unified payload property name
The two DataQuality tiles use `href`; the FailedJobs tile uses `url`. After the change, all three tiles use the same property name (`routeKey`), eliminating the inconsistency.

**Acceptance criteria:**
- All three tiles emit the property as `routeKey` (camelCase, matching the existing anonymous-object convention serialized to JSON).
- No tile emits `href` or `url` in its drill-down payload.

### FR-3: DataQuality tiles converge on a single destination
The two DataQuality tiles currently disagree (`/data-quality` vs `/automation/data-quality`). Both must use the same `routeKey` (`"dataQuality"`), and the frontend mapping resolves that key to **one** canonical path.

**Acceptance criteria:**
- Both `DataQualityStatusTile` and `DqtYesterdayStatusTile` emit `routeKey = "dataQuality"`.
- The frontend route map resolves `"dataQuality"` to `/automation/data-quality` (the path used by the more recent `DqtYesterdayStatusTile`, which matches the current `/automation/` section of the SPA).
- Clicking either tile navigates the user to the same page.

### FR-4: Frontend route-key registry
A single frontend module owns the mapping from `routeKey` strings to actual paths. Tile renderers consult this registry to construct the drill-down link.

**Acceptance criteria:**
- A new module (e.g. `frontend/src/features/dashboard/drillDownRoutes.ts`) exports `drillDownRoutes: Record<string, string>` containing at least:
  - `dataQuality: "/automation/data-quality"`
  - `hangfireFailedJobs: "/hangfire/jobs/failed"`
- The dashboard tile renderer reads `tile.drillDown.routeKey`, looks up the path in `drillDownRoutes`, and renders the link with that path when `enabled` is true.
- If a tile returns a `routeKey` not present in the registry, the renderer treats the drill-down as disabled and logs a console warning (no crash, no broken link).
- Unit test in the frontend asserts the registry contains both keys and resolves to the expected paths.

### FR-5: Backend test coverage updated
Existing tests against the three tiles assert the new payload shape rather than the old `href`/`url` strings.

**Acceptance criteria:**
- All unit/integration tests under `backend/test/**` that reference `DrillDownHref`, `FailedJobsUrl`, `"/data-quality"`, `"/automation/data-quality"`, or `"/hangfire/jobs/failed"` in the context of the three tiles are updated to assert `routeKey`.
- New or updated assertions verify the `routeKey` values defined in FR-1.
- `dotnet test` passes for the affected projects.

### FR-6: Documentation example refreshed
`docs/architecture/development_guidelines.md` currently uses the dashboard-tile case as a "forbidden practice" example. Add a one-line note pointing to the `routeKey` pattern as the prescribed alternative, so the next reviewer sees the resolution.

**Acceptance criteria:**
- The "Backend constructing frontend URLs" entry in `development_guidelines.md` is amended with a short note: backend returns `routeKey`, frontend owns the lookup; reference the `drillDownRoutes` module by relative path.
- No other content in the guidelines is changed.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. The change is a string-substitution at serialization time and an O(1) map lookup at render time. No new network calls, no new dependencies.

### NFR-2: Security
No change to authorization. The `enabled` flag continues to gate drill-down visibility based on the same backend logic as today. The semantic `routeKey` does not leak any new information — it is at least as opaque as the previous path strings.

### NFR-3: Maintainability
The whole point of the change. After the work, restructuring the `/automation/` segment or moving the Data Quality page requires editing exactly one file (`drillDownRoutes.ts`) instead of three backend tile files.

### NFR-4: Backward compatibility
This is an internal payload contract between backend tiles and the frontend dashboard renderer, both shipped in the same Docker image. No external consumers. No transitional `href` field is required — the frontend renderer is updated in lockstep with the backend.

## Data Model
No persistent data model changes. The change affects only the in-memory anonymous object returned by `LoadDataAsync` in three classes and the corresponding TypeScript shape consumed by the dashboard renderer.

Updated payload shape (per tile):

```jsonc
{
  "drillDown": {
    "routeKey": "dataQuality",   // or "hangfireFailedJobs"
    "enabled": true
  }
}
```

If the OpenAPI-generated TypeScript client exposes a typed shape for the drill-down object, that type is updated to `{ routeKey: string; enabled: boolean }` and the regenerated client is committed. If the field is currently typed loosely (e.g. `any`/`object`), the frontend renderer narrows it locally and no client regeneration is needed.

## API / Interface Design

**Backend (C#)** — anonymous object literal in each tile's `LoadDataAsync`:

```csharp
// DataQualityStatusTile.cs (replaces lines 37 and 50)
drillDown = new { routeKey = "dataQuality", enabled = true }

// DqtYesterdayStatusTile.cs (replaces lines 53, 79, 93, and any other usage)
drillDown = new { routeKey = "dataQuality", enabled = true }

// FailedJobsTile.cs (replaces line 45)
drillDown = new { routeKey = "hangfireFailedJobs", enabled = true }
```

**Frontend (TypeScript)** — new registry module:

```ts
// frontend/src/features/dashboard/drillDownRoutes.ts
export const drillDownRoutes: Record<string, string> = {
  dataQuality: "/automation/data-quality",
  hangfireFailedJobs: "/hangfire/jobs/failed",
};

export function resolveDrillDownPath(routeKey: string): string | null {
  return drillDownRoutes[routeKey] ?? null;
}
```

**Dashboard tile renderer** — locate the existing component that reads `tile.drillDown.href` (or `tile.drillDown.url`) and replace the access with `resolveDrillDownPath(tile.drillDown.routeKey)`. Disable the link if the lookup returns `null`.

## Dependencies
- No new libraries.
- Depends on the existing dashboard tile rendering component in the frontend (location to be confirmed during implementation — likely under `frontend/src/features/dashboard/` per `docs/architecture/filesystem.md`).
- Depends on the OpenAPI client regeneration step (`docs/development/api-client-generation.md`) only if the drill-down field is currently typed in the generated client.

## Out of Scope
- Restructuring the `/automation/` URL segment or any other route on the SPA side.
- Centralizing routing for non-dashboard backend code paths (e.g. emails, notifications) that may also construct frontend URLs — those are separate findings.
- Adding new dashboard tiles or new drill-down destinations.
- Changing the `enabled` flag semantics or the tile data sources.
- Internationalization or display-name handling for drill-down links.

## Open Questions
None.

## Status: COMPLETE