### task: frontend-client
**Files:** `frontend/src/api/generated/api-client.ts` (regenerated), `frontend/src/api/hooks/useDashboard.ts` (only if needed)

- [ ] Build backend (regenerates the OpenAPI TS client per `docs/development/api-client-generation.md`); confirm `dashboard_EnableTile`/`dashboard_DisableTile` now return typed responses instead of `FileResponse`, and commit the regenerated file if the repo tracks it.
- [ ] Check `useEnableTile`/`useDisableTile` and `DashboardSettings.tsx` do not depend on `FileResponse`; adjust only if the build fails.
- [ ] Run `cd frontend && npm run build && npm run lint` and the Dashboard-related jest tests.

