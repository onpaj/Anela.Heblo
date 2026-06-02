All 10 implementation commits are on the branch. Here is the output summary:

---

# Implementation: Decouple Dashboard Tile Drill-Downs from Frontend Routing

## What was implemented

Replaced hardcoded frontend URL strings in three backend dashboard tiles with a typed `DashboardTileDrillDown` contract carrying a semantic route key. Added a single frontend route registry (`drillDownRoutes.ts`) and a `resolveDrillDown()` helper that maps route keys to actual paths, supporting both React Router navigation and same-origin external URLs (Hangfire).

## Files created/modified

**Backend (new):**
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs` — plain class with `RouteKey`, `Enabled`, `Parameters?` and `[JsonPropertyName]` attributes for camelCase serialization
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` — new test file (3 tests)

**Backend (modified):**
- `DqtYesterdayStatusTile.cs` — removed `DrillDownHref` constant, emits `DashboardTileDrillDown { RouteKey = "dataQuality" }`
- `FailedJobsTile.cs` — removed `FailedJobsUrl` constant, emits anonymous `{ routeKey = "hangfireFailedJobs", enabled, tooltip }` (retains tooltip)
- `DataQualityStatusTile.cs` — removed `href = "/data-quality"`, emits `DashboardTileDrillDown { RouteKey = "dataQuality" }`
- `DqtYesterdayStatusTileTests.cs` — assertions updated to `routeKey == "dataQuality"` + absence of `href`/`url`
- `FailedJobsTileTests.cs` — assertions updated to `routeKey == "hangfireFailedJobs"` + absence of `href`/`url`, tooltip preserved

**Frontend (new):**
- `frontend/src/components/dashboard/drillDownRoutes.ts` — `DASHBOARD_DRILLDOWN_ROUTES`, `resolveDrillDown()`, all types
- `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx` — 6 resolver tests
- `frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx` — 3 tests

**Frontend (modified):**
- `FailedJobsTile.tsx` — replaced `HANGFIRE_PATH` with resolver; `window.open` guarded by null check
- `DqtYesterdayStatusTile.tsx` — replaced hardcoded `navigate('/automation/data-quality')` with resolver
- `DataQualityTile.tsx` — replaced hardcoded `navigate('/automation/data-quality')` with resolver
- `DqtYesterdayStatusTile.test.tsx` — added `drillDown` to test data, added unknown-key test
- `FailedJobsTile.test.tsx` — added `drillDown` to test data, added no-drill-down guard test

**Documentation:**
- `memory/patterns/dashboard-tile-drilldown.md` — documents the two coexisting drill-down shapes

## Tests

- **Backend:** 45 dashboard tile tests pass (3 tiles × ~7-15 tests each incl. new DataQualityStatusTile tests)
- **Frontend:** 111 dashboard component tests pass across 11 test suites (including 6 resolver tests, 8 DqtYesterdayStatusTile tests, 5 FailedJobsTile tests, 3 DataQualityTile tests)

## How to verify

```bash
# Backend
dotnet test /path/to/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DashboardTiles"
# → 45 passed

# Frontend (from frontend/ directory)
CI=true npm test -- --testPathPattern="src/components/dashboard" --no-coverage
# → 111 passed
```

## Notes

1. `DashboardTileDrillDown` received `[JsonPropertyName]` attributes — necessary because backend tile tests serialize using raw `JsonSerializer.Serialize()` (without camelCase options), so the attributes ensure consistent `routeKey`/`enabled` casing between test and production contexts.
2. `FailedJobsTile` keeps an anonymous object (not the typed DTO) for its `drillDown` payload because the `tooltip` field is not part of `DashboardTileDrillDown`. This is intentional.
3. The 32 pre-existing test failures in the full suite are PostgreSQL testcontainers integration tests that require Docker — unrelated to this change.
4. Lint errors in the dashboard test folder (`DashboardGrid.test.tsx`, `DashboardTile.test.tsx`) are pre-existing; our new files are lint-clean.

## PR Summary

Replaces hardcoded frontend URL strings in three backend dashboard tile handlers (`DataQualityStatusTile`, `DqtYesterdayStatusTile`, `FailedJobsTile`) with a typed `DashboardTileDrillDown` contract carrying a semantic route key. The frontend gains a single source-of-truth route registry (`drillDownRoutes.ts`) and a `resolveDrillDown()` helper that maps keys to actual paths. Changing a drill-down destination now requires editing only the frontend registry — no backend change, no redeploy.

The change also resolves an existing inconsistency: two tiles for the same DataQuality page disagreed on `/data-quality` vs `/automation/data-quality`; both now resolve to `/automation/data-quality` via the registry. The Hangfire tile preserves its cross-origin, new-tab navigation behavior by prepending `apiUrl` in the `external` resolver branch.

### Changes
- `backend/src/.../Dashboard/Contracts/DashboardTileDrillDown.cs` — new typed DTO (plain class, `[JsonPropertyName]` for camelCase)
- `backend/src/.../DataQuality/DashboardTiles/DataQualityStatusTile.cs` — route key replaces `/data-quality` href
- `backend/src/.../DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — route key replaces `/automation/data-quality` href
- `backend/src/.../BackgroundJobs/DashboardTiles/FailedJobsTile.cs` — route key replaces `/hangfire/jobs/failed` url
- `backend/test/.../DataQualityStatusTileTests.cs` — new test file (3 tests)
- `backend/test/.../DqtYesterdayStatusTileTests.cs` — assert `routeKey`, absent `href`/`url`
- `backend/test/.../FailedJobsTileTests.cs` — assert `routeKey`, absent `href`/`url`, tooltip preserved
- `frontend/src/components/dashboard/drillDownRoutes.ts` — route registry + `resolveDrillDown()` resolver
- `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx` — 6 resolver unit tests
- `frontend/src/.../tiles/FailedJobsTile.tsx` + test — resolver-driven click; no-drill-down guard
- `frontend/src/.../tiles/DqtYesterdayStatusTile.tsx` + test — resolver-driven click; unknown-key guard
- `frontend/src/.../tiles/DataQualityTile.tsx` + test — resolver-driven click; unknown-key guard
- `memory/patterns/dashboard-tile-drilldown.md` — documents the two coexisting drill-down shapes

## Status
DONE