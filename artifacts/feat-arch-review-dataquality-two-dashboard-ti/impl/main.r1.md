---

# Implementation: DataQuality Dashboard Tiles Drill-Down Route Unification

## What was implemented

All implementation was already present in the codebase (landed via PR #2278 "Decouple Dashboard Tiles from Frontend Routing"). This session verified full compliance and committed the implementation plan document.

The change replaces hard-coded frontend URLs in two DataQuality dashboard tiles with a semantic `routeKey` contract resolved by the frontend:

- **Shared backend DTO** (`DashboardTileDrillDown`) in `Dashboard.Contracts` namespace — class (not record), camelCase JSON via `[JsonPropertyName]`, no URL members
- **Both tiles** (`DataQualityStatusTile`, `DqtYesterdayStatusTile`) emit `{ routeKey: "dataQuality", enabled: true }` via a single private const across all 3 return paths each
- **Frontend resolver** (`drillDownRoutes.ts`) with closed union `'dataQuality' | 'hangfireFailedJobs'`, maps `dataQuality → /automation/data-quality` (react-router strategy), gracefully degrades unknown keys to null + `console.warn`
- **Both tile components** route through `resolveDrillDown(data.drillDown)` — no hard-coded paths, react-router strategy uses `useNavigate()`, external uses `window.open`

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs` — shared DTO
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — migrated to routeKey
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — migrated to routeKey
- `frontend/src/components/dashboard/drillDownRoutes.ts` — extended with `dataQuality` entry
- `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` — uses `resolveDrillDown`
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` — uses `resolveDrillDown`
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` — 3 xUnit tests
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` — 7 xUnit tests
- `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx` — 6 resolver tests
- `frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx` — 3 tile tests
- `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx` — 8 tile tests
- `docs/superpowers/plans/2026-06-03-dashboard-tile-drilldown-routekey.md` — implementation plan (committed this session)

## Tests

- **Backend**: 55 DataQuality tests pass (10 new tile tests + existing); full backend test suite passes (38 pre-existing database integration test failures in unrelated modules unaffected)
- **Frontend**: 17 new tests pass across 3 suites (`drillDownRoutes`, `DataQualityTile`, `DqtYesterdayStatusTile`)

## How to verify

```bash
# Backend DataQuality tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQuality" --nologo

# Frontend tile + resolver tests
npm --prefix frontend test -- --watchAll=false --testPathPattern="dashboard/__tests__/drillDownRoutes|dashboard/tiles/__tests__/DataQualityTile|dashboard/tiles/__tests__/DqtYesterdayStatusTile"

# Grep-clean check (must return zero matches)
grep -RnE '/data-quality|/automation/' backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/

# Full builds
dotnet build Anela.Heblo.sln --nologo
npm --prefix frontend run build
```

## Notes

- Implementation was already merged to `main` via PR #2278 before this branch was created; the branch inherits the changes. Both spec compliance and code quality reviewers confirmed **COMPLIANT / APPROVED**.
- The 38 failing backend tests are all pre-existing database/SQL integration tests (KnowledgeBase, Photobank, Catalog, Flexi adapter) unrelated to this feature — they require a live database and have been failing on this branch since before it was created.
- `DataQualityStatusTile.cs` catch block silently swallows exceptions (pre-existing asymmetry with `DqtYesterdayStatusTile` which logs); explicitly out of scope per spec and arch review.

## PR Summary

Replaced two hardcoded frontend route strings (`/data-quality` and `/automation/data-quality`) inside the DataQuality dashboard tiles with a semantic `routeKey: "dataQuality"` contract resolved exclusively on the frontend, eliminating backend knowledge of the SPA route layout.

The backend emits a new shared `DashboardTileDrillDown` DTO (class, not record; camelCase JSON) with `routeKey` and `enabled` fields across all response branches of both tiles. The frontend's existing `drillDownRoutes.ts` resolver is extended with the `dataQuality` → `/automation/data-quality` mapping, and both tile components now call `resolveDrillDown()` rather than constructing URLs. Unknown route keys degrade gracefully (null + `console.warn`). Backend xUnit tests lock the wire shape per branch; a grep guard confirms no frontend paths remain in backend tile files.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs` — new shared DTO
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — migrated drillDown payload
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — migrated drillDown payload
- `frontend/src/components/dashboard/drillDownRoutes.ts` — added `dataQuality` registry entry
- `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` — routes through `resolveDrillDown`
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` — routes through `resolveDrillDown`
- Test files for all 6 implementation files above

## Status

DONE