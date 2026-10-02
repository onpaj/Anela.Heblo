---
process: module-configuration
kind: module
module: configuration
summary: Tells the Heblo web app which version and environment it is talking to (shown in the status bar) and lets it notice a new deployment and offer a refresh.
owns: []
verified_at: "f68c439ec"
related: []
---

# Configuration (app version & environment)

## Purpose
A tiny read-only module behind one endpoint. It answers "which Heblo am I using?": the deployed
**version** (e.g. `3.157.0`), the **environment** (Production / Staging / Development) and
whether the backend runs with **mock authentication** (local development and E2E only).

Staff see it in the status bar at the bottom of every page. The web app also polls it every
5 minutes; when the version differs from the one stored in the browser, it shows a toast
"New Version Available" with an action to refresh, so people don't keep working on an old
frontend after a deploy.

It does **not** expose or change any business settings — those live in each module's own
configuration and in the admin pages (feature flags, access management, recurring jobs).

## Users & screens
- Status bar (`frontend/src/components/StatusBar.tsx`, via `useConfigurationQuery`) — every user,
  every page: version (prefixed with `v`) and environment.
- Version check (`frontend/src/services/versionService.ts`, started from `AppInitializer` through
  `useVersionCheck`) — every 5 min; stores the last seen version in `localStorage` (`app_version`)
  and the last 10 already-announced versions (`notified_versions`) so a toast is shown once.

API: `GET /api/Configuration` (`ConfigurationController`) → `{ version, environment, useMockAuth }`.
No MCP tool, no dashboard tile.

## Processes
None — a single read endpoint, no jobs, no writes.

How each value is resolved (`GetConfigurationHandler`):

| Field | Source, first hit wins | Fallback |
|---|---|---|
| `version` | config key `APP_VERSION` (set as an App Setting by the deploy workflows `ci-main-branch.yml` / `deploy-staging-manual.yml`, and `staging-main-<sha>` by `e2e-nightly-regression.yml`) → `AssemblyInformationalVersion` of the Application assembly → assembly version | `"1.0.0"` (`ConfigurationConstants.DEFAULT_VERSION`) |
| `environment` | config key `ASPNETCORE_ENVIRONMENT` | `"Production"` (`DEFAULT_ENVIRONMENT`) |
| `useMockAuth` | config key `UseMockAuth` | `false` |

## Data owned
None. Browser-side only: the two `localStorage` keys above.

## External systems
None.

## Dependencies
- Reads only `IConfiguration` and assembly metadata.
- Read by: the frontend status bar and version check. `UseMockAuth` itself is also read directly
  by authentication, the Hangfire dashboard filter and the Microsoft 365 adapter — not through
  this module.

## Known quirks
- `ConfigurationController` has no `[Authorize]` attribute and there is no global fallback
  policy, so `GET /api/Configuration` answers without authentication (version + environment
  are not sensitive).
- If `ASPNETCORE_ENVIRONMENT` is missing the module reports `Production`, which on a local
  machine is misleading rather than safe.
- The frontend falls back to `REACT_APP_VERSION` or `0.1.0` in the status bar while the request
  is loading or if it fails.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — value resolution
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — fallbacks
- `backend/src/Anela.Heblo.API/Controllers/ConfigurationController.cs` — endpoint
- `frontend/src/services/versionService.ts`, `frontend/src/hooks/useVersionCheck.ts` — new-version toast
- `frontend/src/components/StatusBar.tsx` — status bar
