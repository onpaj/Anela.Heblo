# Specification: DataQuality dashboard tiles drill-down route unification

## Summary
Two DataQuality dashboard tiles (`DataQualityStatusTile`, `DqtYesterdayStatusTile`) historically hard-coded different frontend routes (`/data-quality` vs `/automation/data-quality`) in their drill-down payloads, producing inconsistent navigation and violating the architecture rule against backend-constructed frontend URLs. This specification replaces hard-coded URLs with a semantic key contract (`routeKey`) resolved by the frontend, so the backend no longer encodes any knowledge of the React route layout.

## Background
The DataQuality module ships two dashboard tiles backed by `IDqtRunRepository`: a small "Kvalita dat" tile showing the last DQT run status, and a medium "DQT včera" tile showing yesterday's run status. Both tiles render in the same dashboard and represent the same domain (invoice DQT), so both must drill down to the same destination.

The daily architecture review on 2026-06-01 found two distinct hard-coded `drillDown.href` values across these tiles. Beyond the immediate inconsistency, the project's `docs/architecture/development_guidelines.md` explicitly forbids backend-constructed frontend URLs — they couple the backend to the frontend router and turn every frontend rename into a backend code change. The inconsistency in the brief was the natural symptom of exactly that coupling.

The correct destination route in the React app is `/automation/data-quality` (the DataQuality automation page). The fix is therefore both (a) make the two tiles consistent and (b) move route resolution to the frontend, where it belongs.

## Functional Requirements

### FR-1: Shared drill-down DTO with semantic key
The backend must expose a stable `DashboardTileDrillDown` DTO that carries a semantic `routeKey` (string) plus an `enabled` flag and optional `parameters` map. It must not contain a frontend URL.

**Acceptance criteria:**
- A class (not a record — DTO/OpenAPI rule from CLAUDE.md) named `DashboardTileDrillDown` exists under `Anela.Heblo.Application.Features.Dashboard.Contracts`.
- It exposes `RouteKey: string`, `Enabled: bool`, and `Parameters: IReadOnlyDictionary<string, string>?`.
- JSON property names are camelCase (`routeKey`, `enabled`, `parameters`).
- No member of the DTO carries a path, URL, or other frontend-routing string.

### FR-2: Both DataQuality tiles use the same semantic key
Both `DataQualityStatusTile` and `DqtYesterdayStatusTile` must embed a `DashboardTileDrillDown` with the same `RouteKey` value `"dataQuality"` in every payload they return (success, no-data, and error branches).

**Acceptance criteria:**
- Each tile defines a single `private const string DrillDownRouteKey = "dataQuality";` and references it in every return path.
- Neither tile contains a hard-coded URL, route fragment, or string starting with `/`.
- All three response branches in each tile (data found, `no_data`, exception) include the drill-down payload with `Enabled = true`.

### FR-3: Frontend drill-down resolver
The frontend must expose a single source of truth that maps each known `routeKey` to a concrete destination, and a `resolveDrillDown` function that consumers call.

**Acceptance criteria:**
- A `DashboardDrillDownRouteKey` string-literal union enumerates all known keys (initially `'dataQuality'` and `'hangfireFailedJobs'`).
- A `DASHBOARD_DRILLDOWN_ROUTES` record keyed by `DashboardDrillDownRouteKey` maps each key to a `DrillDownTarget` (`{ type: 'react-router' | 'external', path: string }`).
- `dataQuality` resolves to `{ type: 'react-router', path: '/automation/data-quality' }`.
- `resolveDrillDown` returns `null` when the drill-down is missing, disabled, or carries an unknown key, and logs `console.warn` with the offending key in the unknown case (backend-ahead-of-frontend scenario must not throw).
- External targets are prefixed with the runtime API URL; react-router targets are returned as-is for `<Link>`/`navigate` usage.

### FR-4: Tile components consume the resolver
Both `DataQualityTile.tsx` and `DqtYesterdayStatusTile.tsx` must drive their drill-down behaviour through `resolveDrillDown` and not construct URLs themselves.

**Acceptance criteria:**
- Neither tile component contains a literal path string for the drill-down target.
- When `resolveDrillDown` returns `null`, the tile renders without a navigable drill-down affordance.
- When it returns `{ strategy: 'react-router' }`, navigation uses the SPA router (no full page reload).
- When it returns `{ strategy: 'external' }`, the link opens the external URL (Hangfire pattern).

### FR-5: Graceful degradation on unknown keys
If the backend ships a new `routeKey` before the frontend knows about it, the tile must remain visible and load its data, but must not crash or navigate to a broken URL.

**Acceptance criteria:**
- `resolveDrillDown` returns `null` for unknown keys instead of throwing.
- The unknown key is logged via `console.warn` once per occurrence with the offending key in the message.
- The tile renders status content normally; only the click-to-drill-down affordance is suppressed.

## Non-Functional Requirements

### NFR-1: Performance
No additional backend or network round-trip is introduced. Route resolution is an in-memory lookup on a small object; per-tile resolution must remain O(1) and complete in well under 1 ms.

### NFR-2: Security
The resolver operates on a closed allow-list of known keys. Unknown keys never reach navigation. External targets are anchored to the configured `apiUrl` only; raw user-supplied URLs must never be navigated to.

### NFR-3: Maintainability / architectural fitness
- Backend tile code must contain zero frontend paths (grep-clean for `/data-quality`, `/automation/`, etc. in `backend/src/.../DataQuality/DashboardTiles/`).
- Adding a new dashboard drill-down destination requires touching only the frontend resolver, not the backend.
- The `DashboardDrillDownRouteKey` union is closed: a backend route key without a frontend mapping is a TypeScript build error at the tile consumer site.

### NFR-4: Backward compatibility
Existing tile JSON shape must continue to be readable by clients during a single deploy window. The DTO change is additive on the backend (renaming `href` → `routeKey` is a hard cutover; the frontend deploy must precede or accompany the backend deploy).

## Data Model

No persistence change. The only model change is the API-layer DTO emitted in tile payloads:

```
DashboardTileDrillDown
  routeKey:    string           // semantic key, e.g. "dataQuality"
  enabled:     bool              // false suppresses drill-down affordance
  parameters?: dict<string,string> // optional, for future parameterised routes
```

The tile payload structure remains:
```
{ status, data, drillDown: DashboardTileDrillDown }
```

## API / Interface Design

### Backend (tile payload contract)
Every DataQuality tile returns:
```json
{
  "status": "success" | "warning" | "error" | "no_data",
  "data":   { ... domain fields ... } | null,
  "drillDown": { "routeKey": "dataQuality", "enabled": true }
}
```

### Frontend (resolver contract)
```ts
type DashboardDrillDownRouteKey = 'dataQuality' | 'hangfireFailedJobs';

type DrillDownTarget =
  | { type: 'react-router'; path: string }
  | { type: 'external'; path: string };

interface DrillDownResolution {
  url: string;
  strategy: DrillDownTarget['type'];
}

function resolveDrillDown(drillDown: DashboardTileDrillDown | undefined): DrillDownResolution | null;
```

### Affected files
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`
- `frontend/src/components/dashboard/drillDownRoutes.ts`
- `frontend/src/components/dashboard/tiles/DataQualityTile.tsx`
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`
- Existing tile tests under `frontend/src/components/dashboard/tiles/__tests__/` and `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx`

## Dependencies

- **Dashboard tile framework** (`Anela.Heblo.Xcc.Services.Dashboard`) — `ITile`, `TileId`, `TileSize`, `TileCategory` interfaces, unchanged by this work.
- **`IDqtRunRepository`** — unchanged; supplies the data both tiles render.
- **Runtime config** (`frontend/src/config/runtimeConfig.ts`) — provides `apiUrl` for external targets.
- **React Router** — consumers of `resolveDrillDown` must use `Link`/`navigate` for `react-router` strategy.
- **Frontend route `/automation/data-quality`** must exist in the React router and serve the DataQuality automation page. This is the existing destination; no router change is required.

## Out of Scope

- Migrating non-DataQuality tiles that may still hard-code routes. Hangfire (`hangfireFailedJobs`) is already represented in the resolver; other modules' tiles are tracked separately.
- Adding `parameters` consumers — the field exists in the DTO for future use but no current tile needs it.
- Changing tile visual design, status semantics, status strings, or the underlying repository contract.
- Backend integration tests for tile payload shape beyond confirming `routeKey` is emitted (the contract is verified at the type boundary).
- Telemetry / analytics on drill-down clicks.
- Renaming the destination route from `/automation/data-quality` — the route stays where it is; only how tiles refer to it changes.

## Open Questions

None.

## Status: COMPLETE