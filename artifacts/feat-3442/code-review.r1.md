## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/api/hooks/useDashboard.ts:14` — `DashboardTile.size` was widened from the literal union `'Small' | 'Medium' | 'Large'` to plain `string`. The spec (FR-6) explicitly recommended keeping the narrower union via the mapping function to preserve compile-time safety for consumers that switch on this field (`DashboardTile.tsx:42`, `SizeBadge` in `DashboardSettings.tsx:148`). Functionally harmless today — both consumers just do string comparisons/switches with a sane `default` — but it drops exhaustiveness checking and lets an unexpected `size` value from the backend pass through silently instead of being caught at the type level. Consider `size: dto.size as DashboardTile['size'] ?? 'Medium'` with the union type restored, or an explicit runtime guard.
- `frontend/src/api/hooks/useDashboard.ts:60-64` — `isForbidden` narrows `unknown` via `'status' in error` plus a manual cast; this duplicates the check that could be done more directly with `SwaggerException.isSwaggerException(error)` (a static type guard already exported from `../generated/api-client`), which would be more self-documenting and avoid the double cast. Not required — the current structural check is correct and matches what the spec asked for — just a minor simplification opportunity.

### Verification performed
- Confirmed `dashboard_GetAvailableTiles/GetUserSettings/GetTileData/SaveUserSettings/EnableTile/DisableTile` signatures in `frontend/src/api/generated/api-client.ts` match the call sites and argument shapes used in the hook.
- Confirmed `SwaggerException` (api-client.ts:41216) carries a plain numeric `status` field, so `isForbidden` correctly detects 403s thrown by the typed client (`throwException` only special-cases `result`-carrying error bodies, which don't apply to these three GET/POST-returning-FileResponse endpoints).
- Confirmed `getAuthenticatedApiClient()` is synchronous (`client.ts:276`), so dropping `await` in `useAvailableTiles`/`useSaveDashboardSettings`/`useEnableTile`/`useDisableTile` is correct and matches the spec's stated intent (FR-1/FR-4/FR-5).
- Confirmed `getAuthenticatedApiClient(false)` preserves toast-suppression semantics identical to the pre-refactor raw-fetch calls in `useUserDashboardSettings`/`useTileData`.
- Confirmed backend `DashboardController.SaveUserSettings/EnableTile/DisableTile` return bare `Ok()`, matching the spec's explanation for why the generated methods return `Promise<FileResponse>` — no information is lost versus the old hand-rolled fetch, which never inspected the mutation response bodies either.
- Confirmed no caller (`DashboardSettings.tsx`, `Dashboard.tsx`) consumes the mutations' resolved values, so the `FileResponse` return-type change is a non-breaking internal detail.
- Ran `npm run build` — compiles successfully with no type errors.
- Ran the updated `useDashboard.test.tsx` suite — all 17 tests pass, including new 403-fallback tests for `useUserDashboardSettings`/`useTileData`.
