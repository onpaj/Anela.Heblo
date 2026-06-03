Implementation is already present in this worktree. I have enough context to write the architecture review now.

```markdown
# Architecture Review: DataQuality Dashboard Tiles Drill-Down Route Unification

## Skip Design: true

No new UI components, screens, layouts, or visual decisions are introduced. The tiles already exist; only the routing contract between backend and frontend changes. Click affordance is the same as today.

## Architectural Fit Assessment

The proposal is a **generalisation of an existing pattern, not a new one.** `FailedJobsTile` already emits `routeKey: "hangfireFailedJobs"` and the frontend's `FailedJobsTile.tsx` already calls `resolveDrillDown` (`frontend/src/components/dashboard/tiles/FailedJobsTile.tsx:20`). The spec extracts that pattern into a typed `DashboardTileDrillDown` DTO and pulls the DataQuality tiles into it. This is the right direction.

The dashboard currently has **two coexisting drill-down patterns**:

1. **Tile-id-keyed in `TileContent.tsx`** — backend returns `filters: { ... }`; the frontend's `TileContent.tsx` switch maps each `tileId` to a hardcoded `targetUrl` prop (e.g. `frontend/src/components/dashboard/tiles/TileContent.tsx:45-71`). This is used by `Catalog`, `Logistics`, `Manufacture`, `Analytics`, and `Purchase` tiles.
2. **Route-key based** — backend returns `routeKey: "<semantic-key>"`; the frontend resolver maps the key to a target. Used today only by `FailedJobsTile` and (per this spec) the two DataQuality tiles.

Pattern (2) is strictly superior — `TileContent.tsx` should not be the registry of frontend routes — but the spec correctly scopes out migrating other tiles. The integration point this change touches is therefore narrow and well-bounded.

The destination route `/automation/data-quality` is confirmed in `frontend/src/App.tsx:474` (mounted to `DataQualityPage`). The sidebar already links there (`Sidebar.tsx:339`). The brief's "at least one is wrong" claim is correct: `/data-quality` (the small tile's old value) does not exist; `/automation/data-quality` is the live route.

The DTO rule (class, not record) and the JSON-shape rule (camelCase on the wire) are both honoured by the existing `DashboardTileDrillDown.cs` via `[JsonPropertyName]` attributes. Embedding the DTO inside the tile's anonymous projection (returned as `Task<object>`) is consistent with how every other tile in the codebase emits its payload.

## Proposed Architecture

### Component Overview

```
                    ┌──────────────────────────────────────────────┐
                    │ Backend (Anela.Heblo.Application)            │
                    │                                              │
                    │  Features/Dashboard/Contracts/               │
                    │    DashboardTileDrillDown                    │
                    │      routeKey: string                        │
                    │      enabled:  bool                          │
                    │      parameters: dict<string,string>?        │
                    │                                              │
                    │  Features/DataQuality/DashboardTiles/        │
                    │    DataQualityStatusTile  ─┐                 │
                    │    DqtYesterdayStatusTile ─┤                 │
                    │                            │                 │
                    │   both emit:               ▼                 │
                    │   { status, data, drillDown: {               │
                    │     routeKey: "dataQuality", enabled: true } │
                    └──────────────┬───────────────────────────────┘
                                   │  HTTP JSON
                                   ▼
                    ┌──────────────────────────────────────────────┐
                    │ Frontend (React / TS)                        │
                    │                                              │
                    │  components/dashboard/drillDownRoutes.ts     │
                    │    type DashboardDrillDownRouteKey =         │
                    │      'dataQuality' | 'hangfireFailedJobs'    │
                    │    DASHBOARD_DRILLDOWN_ROUTES (closed map)   │
                    │    resolveDrillDown(dd) → Resolution | null  │
                    │           │                                  │
                    │           │ used by                          │
                    │           ▼                                  │
                    │  components/dashboard/tiles/                 │
                    │    DataQualityTile.tsx                       │
                    │    DqtYesterdayStatusTile.tsx                │
                    │    FailedJobsTile.tsx                        │
                    │      → useNavigate() | window.open()         │
                    └──────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Semantic key in DTO, not a URL

**Options considered:**
- (A) Shared constant in backend (`DataQualityConstants.DrillDownRoute = "/automation/data-quality"`).
- (B) Semantic `routeKey` resolved on the frontend.

**Chosen approach:** (B), per spec.

**Rationale:** (A) fixes the inconsistency but keeps the forbidden coupling (`docs/architecture/development_guidelines.md:41`). (B) breaks the coupling: a frontend rename never again forces a backend code change, and the backend module owns no string that even *looks* like a frontend path. The pattern is already proven by `FailedJobsTile`.

#### Decision 2: Closed string-literal union with runtime fallback

**Options considered:**
- (A) Open `string`, no compile-time check, no runtime check — fail loudly on unknown keys.
- (B) Open `string` on the DTO, closed union in the registry, **runtime null+warn** on unknown keys (graceful degradation).
- (C) Closed union end-to-end (typed code-gen of the DTO too).

**Chosen approach:** (B). The DTO carries `routeKey: string` (open); the resolver narrows to the closed `DashboardDrillDownRouteKey` union; unknown keys log `console.warn` and return `null`.

**Rationale:** (C) would force the backend to ship before the frontend in any release that adds a key — fragile and not enforceable in OpenAPI-generated TS. (A) crashes the dashboard if the backend ships a new key first. (B) keeps the dashboard alive (the rest of the tile still renders), tells developers loudly via `console.warn`, and gives a compile-time signal at the *registry* site whenever the union is extended without a mapping. The "compile-time error at the tile consumer site" wording in NFR-3 is slightly aspirational — see Specification Amendments below.

#### Decision 3: Hard cutover of the wire format (no `href` legacy)

**Options considered:**
- (A) Emit both `href` (legacy) and `routeKey` (new), drop `href` after a deploy.
- (B) Hard cutover — backend emits `routeKey` only, frontend reads `routeKey` only.

**Chosen approach:** (B), and it's safe here because the deployment artefact is a **single Docker image** (`CLAUDE.md` "Project facts"). Frontend and backend ship together. NFR-4's "frontend deploy must precede or accompany the backend deploy" is automatically satisfied by the build pipeline. The dual-emit complication in NFR-4 is unnecessary — it should be removed.

## Implementation Guidance

### Directory / Module Structure

Already in place in this branch:

```
backend/src/Anela.Heblo.Application/Features/
├── Dashboard/Contracts/
│   └── DashboardTileDrillDown.cs         ← shared DTO, owned by Dashboard module
└── DataQuality/DashboardTiles/
    ├── DataQualityStatusTile.cs          ← uses DashboardTileDrillDown
    └── DqtYesterdayStatusTile.cs         ← uses DashboardTileDrillDown

frontend/src/components/dashboard/
├── drillDownRoutes.ts                    ← resolver + closed registry
├── tiles/
│   ├── DataQualityTile.tsx               ← consumes resolveDrillDown
│   ├── DqtYesterdayStatusTile.tsx        ← consumes resolveDrillDown
│   └── __tests__/
│       ├── DataQualityTile.test.tsx
│       └── DqtYesterdayStatusTile.test.tsx
└── __tests__/
    └── drillDownRoutes.test.tsx
```

The DTO living under `Features/Dashboard/Contracts/` (not under `DataQuality/`) is correct: it is a Dashboard-module contract consumed by every tile, not a DataQuality concept. Cross-module reuse of this DTO follows the consumer-owns-contract direction (`development_guidelines.md` § "Cross-Module Communication").

### Interfaces and Contracts

**Backend DTO (final):**

```csharp
public class DashboardTileDrillDown
{
    [JsonPropertyName("routeKey")]
    public string RouteKey { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string>? Parameters { get; set; }
}
```

`class` (not `record`) is mandatory — OpenAPI-generated TS clients mishandle record constructors (CLAUDE.md). `Parameters` is optional and nullable; tiles that don't need it omit it.

**Frontend contract (final, in `drillDownRoutes.ts`):**

```ts
type DashboardDrillDownRouteKey = 'dataQuality' | 'hangfireFailedJobs';

type DrillDownTarget =
  | { type: 'react-router'; path: string }
  | { type: 'external';     path: string };

interface DashboardTileDrillDown {
  routeKey: string;
  enabled: boolean;
  parameters?: Record<string, string>;
}

interface DrillDownResolution {
  url: string;
  strategy: DrillDownTarget['type'];
}

function resolveDrillDown(
  drillDown: DashboardTileDrillDown | undefined
): DrillDownResolution | null;
```

`external` targets are prefixed with `getConfig().apiUrl` — only because Hangfire is served from the API host on a different port than the SPA. `react-router` targets are returned bare for `useNavigate()`. This dual-strategy design must stay — collapsing both into a single string would break the `<Link>` vs `window.open` distinction.

### Data Flow

```
1.  GET /api/dashboard/tile/dataqualitystatus
2.  DataQualityStatusTile.LoadDataAsync()
       ↳ IDqtRunRepository.GetLatestByTestTypeAsync(...)
       ↳ returns { status, data, drillDown: { routeKey: "dataQuality", enabled: true } }
3.  Frontend useDashboardTileData() → tile.data
4.  TileContent switch → <DataQualityTile data={tile.data} />
5.  DataQualityTile:
       const resolution = resolveDrillDown(data.drillDown)
         → { url: '/automation/data-quality', strategy: 'react-router' }
6.  onClick → navigate('/automation/data-quality')   // SPA, no reload
```

Error and `no_data` branches follow the same path with `drillDown` still emitted — the tile body changes but the affordance remains clickable.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Other tiles still hard-code routes via `TileContent.tsx` switch | Low | Out of scope per spec; track as a follow-up to migrate `Catalog`/`Logistics`/`Manufacture`/`Analytics`/`Purchase` tiles to `routeKey`. The two patterns coexist safely. |
| NFR-3 claim "TypeScript build error at the tile consumer site" overstates compile-time enforcement — tiles type `data.drillDown.routeKey` as `string`, so a backend-only new key compiles fine | Low | Behaviour matches FR-5 (graceful degradation + warn). Soften NFR-3 wording — see amendment below. The compile-time check exists at the *registry* and at the union itself, which is enough to prevent the frontend from ever silently navigating to a stale route. |
| `console.warn` not deduplicated — a tile that re-fetches will spam the console | Low | Acceptable for an "edge case the frontend wasn't built for" signal. If it becomes noisy in practice, add a `Set<string>` of already-warned keys at module scope. |
| Backend ships new `routeKey` before frontend in a release | None | Single-image deployment (`CLAUDE.md`) ships both together. NFR-4's dual-emit guidance is unnecessary and should be removed. |
| `DataQualityStatusTile.cs` `catch` block swallows the exception silently | Low | Pre-existing behaviour, not introduced here. Out of scope but worth flagging — `DqtYesterdayStatusTile.cs:85` logs; the small tile should too, in a follow-up. |
| Unit-test coverage of the backend tiles is delegated to "the contract is verified at the type boundary" (spec § Out of Scope) | Medium | The C# DTO has no test that locks `routeKey == "dataQuality"`. A trivial xUnit test asserting the emitted JSON shape for each tile would prevent a silent regression and aligns with the project's 80% coverage policy. Recommend adding it despite the spec's exclusion. |

## Specification Amendments

1. **NFR-3 wording** — "a backend route key without a frontend mapping is a TypeScript build error at the tile consumer site" overstates the design. The DTO field is `routeKey: string`, so an unknown key compiles cleanly at the consumer. The compile-time guarantee is at the *registry* (`DASHBOARD_DRILLDOWN_ROUTES: Record<DashboardDrillDownRouteKey, DrillDownTarget>`): adding a key to the union without a mapping is a build error, and removing a mapping that's still referenced as a string literal is a build error. The runtime guarantee at the consumer site (`null` + `console.warn`) is what protects the user. Rephrase: "Extending `DashboardDrillDownRouteKey` without a corresponding entry in `DASHBOARD_DRILLDOWN_ROUTES` is a TypeScript build error. At runtime, unknown keys are degraded to `null` and logged."

2. **NFR-4 dual-emit** — remove. The single-Docker-image deployment makes coordinated frontend+backend cutover automatic. The current `href → routeKey` rename is safe to land in one PR.

3. **Add a backend payload test** (FR-2 § "Out of Scope" carve-out) — at least one xUnit test per tile asserting the emitted JSON contains `drillDown.routeKey == "dataQuality"` and `drillDown.enabled == true` for every status branch (success / no_data / error). The contract is currently held only by the C# constant and the frontend tests; a backend test closes the loop and protects against an accidental rename of the constant.

4. **Add a maintainability lint** (NFR-3 § "grep-clean") — codify the rule as a one-line check in CI or a pre-commit hook:

   ```
   grep -RnE '/data-quality|/automation/' backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/
   ```

   Should always return zero matches. Without enforcement the rule decays.

## Prerequisites

- **No infrastructure changes.** No migration, no config, no Key Vault secrets.
- **No new route.** `/automation/data-quality` already exists (`App.tsx:474`).
- **No new API surface.** Same `GetTileData` use case, same envelope, only the inner `drillDown` shape changes.
- **OpenAPI client regeneration** — the existing `DashboardTileDrillDown.cs` is already a public DTO. Once any controller signature references it (today none do; it's only embedded in anonymous projections), the generated TS client will include it. Until then, the frontend keeps its own `DashboardTileDrillDown` interface in `drillDownRoutes.ts`. That's acceptable as long as the two definitions are kept in sync — flag for the implementer.
- **Coordinated commit** — backend and frontend changes must land in a single PR/commit to avoid a stale-frontend window in a hypothetical hot-reload dev environment. In CI/CD this is a non-issue.
```