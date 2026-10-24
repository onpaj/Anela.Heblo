# frontend-client r1

- Built `Anela.Heblo.API` and regenerated the OpenAPI TS client (`dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`, after `dotnet tool restore`).
- `frontend/src/api/generated/api-client.ts`: `dashboard_EnableTile` / `dashboard_DisableTile` now return `Promise<EnableTileResponse>` / `Promise<DisableTileResponse>` with `Accept: application/json` instead of `FileResponse`.
- `useEnableTile` / `useDisableTile` / `DashboardSettings.tsx`: no dependence on `FileResponse`; no changes needed.
- Verification: `tsc` reports no errors in `src/` (only environment noise from node_modules typings/TS version); eslint clean on touched files (repo-wide lint has pre-existing errors in unrelated test files); jest `--testPathPattern=ashboard` matched 420 suites, all passed (3727 tests).
- Note: `npm ci` needed `--legacy-peer-deps` in this environment (ERESOLVE).
