# Implementation: replace-raw-fetch-with-typed-client-and-mapping-functions

## What was implemented
Migrated all six hooks in `frontend/src/api/hooks/useDashboard.ts` off raw `(apiClient as any).http.fetch(...)` calls and onto the typed generated API client methods (`dashboard_GetAvailableTiles`, `dashboard_GetUserSettings`, `dashboard_SaveUserSettings`, `dashboard_GetTileData`, `dashboard_EnableTile`, `dashboard_DisableTile`). Kept the local `DashboardTile`, `UserDashboardTile`, `UserDashboardSettings`, `SaveDashboardSettingsRequest` interfaces (per design doc, since consumers rely on non-optional fields), widened `DashboardTile.size` from the `'Small' | 'Medium' | 'Large'` literal union to `string`, and added two mapping functions (`toDashboardTile`, `toUserDashboardSettings`) plus a structural `isForbidden(error)` helper to bridge the generated DTOs (all-optional fields) back to the required-field local interfaces at the hook boundary.

All 11 steps from the task context were applied exactly as specified — the pre-edit file content matched the "before" code blocks verbatim, so no deviations were needed.

## Files created/modified
- `frontend/src/api/hooks/useDashboard.ts` — replaced imports (added generated DTO/request-type imports), widened `DashboardTile.size` to `string`, added `toDashboardTile`/`toUserDashboardSettings`/`isForbidden` helpers, and rewrote all six hook bodies (`useAvailableTiles`, `useUserDashboardSettings`, `useTileData`, `useSaveDashboardSettings`, `useEnableTile`, `useDisableTile`) to call the typed generated client methods instead of raw fetch with manually constructed URLs.

## Tests
No test files were touched in this task (a separate task, `update-tests-for-typed-client-methods`, covers test updates per `artifacts/feat-3442/state.json`).

## How to verify
1. `cd frontend && npx tsc --noEmit` — passes with zero errors from `useDashboard.ts` or any consumer (`DashboardSettings.tsx`, `Dashboard.tsx`, `DashboardGrid.tsx`, `DashboardTile.tsx`). Only pre-existing, unrelated `tsconfig.json` deprecation warnings (`target=ES5`, `moduleResolution=node10`) are emitted — 3 lines total, no actual type errors.
2. `grep -n "as any" frontend/src/api/hooks/useDashboard.ts` — no matches (confirmed via exit code 1).

## Notes
No deviations from the task context were required — the file's pre-edit state matched the documented "before" blocks exactly, and all referenced generated-client methods/types (`DashboardTileDto`, `UserDashboardSettingsDto`, `SaveUserSettingsRequest`, `UserDashboardTileDto`, and the six `dashboard_*` methods) were confirmed present in `frontend/src/api/generated/api-client.ts` before editing. `getAuthenticatedApiClient` was confirmed synchronous in `frontend/src/api/client.ts`, consistent with removing the stray `await` calls per Steps 4, 7, 8, 9.

The commit also picked up an unrelated, pipeline-managed change to `artifacts/feat-3442/state.json` (task status tracking, `pending` -> `in_progress`) since the task instructions specified `git add -A`.

## Status
DONE
