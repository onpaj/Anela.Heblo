# Specification: Decouple Dashboard Tiles from Frontend Routing

## Summary
Replace hardcoded frontend URL paths in dashboard tile backend code with semantic route keys that the frontend resolves to actual paths. This restores the separation of concerns mandated by `development_guidelines.md` and eliminates the existing drift between two DataQuality tiles that use different paths for the same page.

## Background
The dashboard tile system uses MediatR-backed tiles in the .NET backend that return a `DrillDown` descriptor consumed by the React frontend. Today three tiles embed literal frontend route strings (`/data-quality`, `/automation/data-quality`, `/hangfire/jobs/failed`) in their payloads. This violates the documented "forbidden practice" of *Backend constructing frontend URLs* and has already produced visible drift: two tiles for the same DataQuality page disagree on whether the path is `/data-quality` or `/automation/data-quality`.

The fix is structural, not cosmetic: the backend should communicate **intent** (which screen to open, with what filter context), and the frontend should own the URL map. Refactoring now is cheap; doing it after more tiles adopt the same pattern is not.

## Functional Requirements

### FR-1: Introduce a `DrillDown` contract with a semantic route key
The backend must expose a typed `DrillDown` DTO (class, not record — per project rule) used by all dashboard tiles. It carries a **route key**, an **enabled** flag, and an optional **parameters** bag for filter/context values. It must not carry any URL, path, or path fragment.

**Acceptance criteria:**
- A `DashboardTileDrillDown` class exists in the shared dashboard tile contracts location (alongside the existing tile DTOs).
- The class has: `string RouteKey`, `bool Enabled`, `IReadOnlyDictionary<string, string>? Parameters` (nullable, omitted when empty).
- The class is a plain class with public init-able properties — not a `record` — so the generated TypeScript client deserializes positionally without breakage.
- No property named `href`, `url`, `path`, or similar appears on the contract.
- The OpenAPI-generated TypeScript type reflects the new shape.

### FR-2: Refactor `DataQualityStatusTile` to use route key
Replace the two `drillDown = new { href = "/data-quality", enabled = true }` anonymous objects in `DataQualityStatusTile.cs` (lines 37, 50) with the `DashboardTileDrillDown` DTO carrying `RouteKey = "dataQuality"`.

**Acceptance criteria:**
- Both return paths in `LoadDataAsync` use `DashboardTileDrillDown` with `RouteKey = "dataQuality"` and `Enabled = true`.
- No string literal starting with `/` appears in this file.
- The tile renders correctly in the dashboard and clicking it navigates to the same destination as `DqtYesterdayStatusTile` (resolving the existing inconsistency in favor of `/automation/data-quality`).

### FR-3: Refactor `DqtYesterdayStatusTile` to use route key
Replace `private const string DrillDownHref = "/automation/data-quality";` and its four usages (lines 9, 53, 79, 93) in `DqtYesterdayStatusTile.cs` with the `DashboardTileDrillDown` DTO carrying `RouteKey = "dataQuality"`.

**Acceptance criteria:**
- The `DrillDownHref` constant is removed.
- All four anonymous-object `drillDown` payloads are replaced with `DashboardTileDrillDown { RouteKey = "dataQuality", Enabled = true }`.
- No string literal starting with `/` appears in this file.

### FR-4: Refactor `FailedJobsTile` to use route key
Replace `private const string FailedJobsUrl = "/hangfire/jobs/failed";` and the `drillDown = new { url = FailedJobsUrl, enabled = true }` (lines 9, 45) in `FailedJobsTile.cs` with the `DashboardTileDrillDown` DTO carrying `RouteKey = "hangfireFailedJobs"`.

**Acceptance criteria:**
- The `FailedJobsUrl` constant is removed.
- The drill-down payload uses `DashboardTileDrillDown { RouteKey = "hangfireFailedJobs", Enabled = true }`.
- No string literal starting with `/` appears in this file.
- The note that this URL points to Hangfire (an external-to-React-router admin UI mounted under the same origin) is preserved in the frontend route map — see FR-5.

### FR-5: Frontend route key registry
The frontend must own a single source-of-truth map from route keys to actual paths. The map handles both React Router internal routes and same-origin admin URLs like Hangfire.

**Acceptance criteria:**
- A file at `frontend/src/dashboard/drillDownRoutes.ts` (or the existing dashboard feature folder if one exists) exports a typed `Record<DashboardDrillDownRouteKey, DrillDownTarget>`.
- Initial entries: `dataQuality → { type: "react-router", path: "/automation/data-quality" }`, `hangfireFailedJobs → { type: "external", path: "/hangfire/jobs/failed" }`.
- The `DashboardDrillDownRouteKey` type is a union of string literals — adding a key the backend sends without a frontend entry is a TypeScript build error.
- A helper (e.g. `resolveDrillDown(drillDown)`) returns the resolved URL plus a navigation strategy (`react-router` uses `useNavigate`; `external` uses `window.location.assign` or similar) so tile components don't repeat the branch logic.

### FR-6: Frontend tile component consumes route key, not URL
The dashboard tile components that render drill-down links must be updated to call `resolveDrillDown(...)` on the new payload shape rather than reading `href` or `url` from the response.

**Acceptance criteria:**
- No frontend tile component reads a `href` or `url` property from a dashboard tile drill-down payload.
- Clicking a drill-down element on each of the three affected tiles navigates to the same destination it did before this change (modulo the deliberate `/data-quality` → `/automation/data-quality` consolidation noted in FR-2).
- An unknown route key logs a warning and renders the tile as non-interactive (drill-down disabled) rather than crashing.

### FR-7: Unknown / unmapped route keys fail safely
If the backend returns a `RouteKey` the frontend does not recognize at runtime (e.g. backend deploys ahead of frontend), the tile must remain functional and visible — only the drill-down action is suppressed.

**Acceptance criteria:**
- `resolveDrillDown` returns `null` (or equivalent) for unknown keys.
- The tile component treats `null` resolution as `Enabled = false` visually.
- A `console.warn` is emitted with the unknown key name to aid diagnostics.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change is expected. Tile payloads grow by at most a few bytes (route key strings are similar in length to the paths they replace). Frontend route resolution is an O(1) dictionary lookup.

### NFR-2: Security
No new security surface. Route keys are not user-controlled; they originate from backend code and are mapped to a static, frontend-defined registry. The map deliberately uses a closed set of literal keys so a compromised or buggy backend cannot induce navigation to an arbitrary URL.

### NFR-3: Maintainability
After this change, modifying a dashboard drill-down destination must be possible by editing **only** the frontend route registry. Verification: changing `/automation/data-quality` to a new path requires no backend file edit and no backend redeploy.

### NFR-4: Test coverage
Existing tile unit tests must be updated to assert on the new contract. New tests must cover the frontend resolver.

**Acceptance criteria:**
- Backend tests for the three tiles assert `RouteKey` and `Enabled` values on the returned `DrillDown`, not URL strings.
- Frontend tests cover `resolveDrillDown` for: known react-router key, known external key, unknown key (returns null + warns).
- Project-wide 80% coverage threshold is preserved.

## Data Model

### `DashboardTileDrillDown` (backend DTO)
| Field | Type | Notes |
|---|---|---|
| `RouteKey` | `string` (required) | Semantic identifier; matches an entry in the frontend registry |
| `Enabled` | `bool` (required) | Whether the tile's drill-down action is currently actionable |
| `Parameters` | `IReadOnlyDictionary<string, string>?` | Optional filter/context bag; omitted when null or empty |

Plain class. Public init-able properties. Not a `record`. Lives next to the existing dashboard tile contract types in `Anela.Heblo.Application` (exact folder to be confirmed during implementation; reuse the location of the existing tile result DTOs).

### Frontend route registry
| Key | Type | Path |
|---|---|---|
| `dataQuality` | `react-router` | `/automation/data-quality` |
| `hangfireFailedJobs` | `external` | `/hangfire/jobs/failed` |

The `type` discriminator drives the navigation strategy: React Router for app-internal pages, full-page navigation for the same-origin Hangfire admin UI (Hangfire is mounted as a separate handler, not part of the React app).

## API / Interface Design

### Tile response shape (before)
```jsonc
{
  "drillDown": { "href": "/data-quality", "enabled": true }
  // or
  "drillDown": { "url": "/hangfire/jobs/failed", "enabled": true }
}
```

### Tile response shape (after)
```jsonc
{
  "drillDown": { "routeKey": "dataQuality", "enabled": true }
}
```

With optional parameters (not used by the three tiles in this scope, but supported by the contract for future tiles):
```jsonc
{
  "drillDown": {
    "routeKey": "dataQuality",
    "enabled": true,
    "parameters": { "severity": "error" }
  }
}
```

### Frontend resolver signature (illustrative)
```ts
type DashboardDrillDownRouteKey = "dataQuality" | "hangfireFailedJobs";

type DrillDownTarget =
  | { type: "react-router"; path: string }
  | { type: "external"; path: string };

function resolveDrillDown(
  drillDown: { routeKey: string; enabled: boolean; parameters?: Record<string, string> }
): { url: string; strategy: DrillDownTarget["type"] } | null;
```

## Dependencies
- Existing dashboard tile infrastructure (`Anela.Heblo.Application/Features/.../DashboardTiles/*`).
- OpenAPI TypeScript client generation pipeline (regenerates on build per `docs/development/api-client-generation.md`).
- React Router (already used by the frontend for in-app navigation).
- Hangfire dashboard mount path (unchanged; only the place that references it moves from backend to frontend).

## Out of Scope
- Refactoring **other** dashboard tiles that do not currently return drill-down URLs. Only the three tiles named in the brief are in scope; an audit pass to ensure no other tile emits a URL is part of the work, but introducing route keys for tiles that don't have drill-downs today is not.
- Adding new drill-down destinations or new tile types.
- Changing the visual appearance of tiles.
- Renaming the existing `/automation/data-quality` route or restructuring the `/automation/` URL segment.
- Building a generic backend-driven navigation framework beyond the dashboard tile drill-down use case.
- Migrating Hangfire away from its current `/hangfire/...` mount point.

## Open Questions
None.

## Status: COMPLETE