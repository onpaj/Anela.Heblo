# Plan — StatusBar: replace `as any` config fetch with `useConfigurationQuery`

## Summary
`frontend/src/components/StatusBar.tsx` currently fetches `/api/configuration` by casting the generated `ApiClient` to `any` and reaching into its private `.baseUrl`/`.http.fetch` internals, producing an untyped response. The fix is to delete that manual fetch path and consume the existing, unused `useConfigurationQuery` hook (`frontend/src/api/hooks/useConfiguration.ts`), which already wraps the typed generated method `apiClient.configuration_GetConfiguration()` in a `useQuery`.

## Context
This is the second occurrence of the same anti-pattern (first was `useManufacturingStockAnalysis`, issue #3730). `StatusBar` renders app version/environment/mock-auth badges in the footer on every page; a silent type mismatch here would misinform the user without any compile-time signal. Fixing it also un-orphans `useConfigurationQuery`, which is exported but has zero current call sites.

## Functional requirements

**FR-1 — Replace manual fetch with `useConfigurationQuery`.**
- `StatusBar` imports and calls `useConfigurationQuery()` from `../api/hooks/useConfiguration` instead of building a request via `getAuthenticatedApiClient()` internals.
- Acceptance: no `as any` cast remains in `StatusBar.tsx`; `apiClient.http` / `apiClient.baseUrl` are no longer referenced from this file.

**FR-2 — Preserve existing display behavior and fallback semantics.**
- When `useConfigurationQuery` has resolved data, the footer must show `data.version`, `data.environment`, and mock-auth badge state exactly as today (same formatting: `v` prefix logic, environment casing/labels, `Mock`/`Mock Auth` badge when `useMockAuth` is true).
- While the query is loading, or if it errors, the component must fall back to the current frontend-only defaults (`process.env.REACT_APP_VERSION || "0.1.0"`, `config.useMockAuth`-derived environment, `config.apiUrl`) rather than rendering nothing or crashing — today's component returns `null` until `appInfo` is set; decide (see Open Questions) whether to keep that behavior or render immediately with fallback values.
- Acceptance: with backend `/api/configuration` reachable, footer shows backend-provided version/environment/mockAuth. With the query failing (e.g. network error, simulated in a test by mocking the hook to return `isError: true`), footer still renders using the local `getRuntimeConfig()`-derived fallback, matching current error-path output.

**FR-3 — Remove now-dead manual-fetch code.**
- Delete the `useEffect`/`useState` block (current lines 19–24, 39–90) once its responsibilities are covered by the hook plus local fallback derivation.
- `getAuthenticatedApiClient` import is removed from `StatusBar.tsx` if no longer used elsewhere in the file.
- Acceptance: `frontend/src/components/StatusBar.tsx` no longer imports `useState`/`useEffect` for this purpose (health-check hooks are untouched), and no longer imports `getAuthenticatedApiClient`.

**FR-4 — `apiUrl` field continuity.**
- `GetConfigurationResponse` (generated client) does not include `apiUrl` — today's code always sources `apiUrl` from local `getRuntimeConfig()`, never from the backend response. Keep sourcing `apiUrl` from `getRuntimeConfig()` regardless of query state.
- Acceptance: the "API: {host}" segment in the non-mobile view continues to reflect `getRuntimeConfig().apiUrl`, unchanged by this refactor.

## Non-functional requirements
- **Type safety**: eliminate all `as any` in this file; `configData` must be typed as `GetConfigurationResponse | undefined` end to end.
- **No new network behavior**: `useConfigurationQuery` uses `staleTime: Infinity, gcTime: Infinity, retry: 1` — one request per session, cached across remounts (StatusBar may mount once, so behaviorally similar to today's one-shot `useEffect` fetch, but now shared/deduped with any other consumer via TanStack Query's cache).
- **No new console noise**: today's code does `console.warn` on fetch failure; decide whether to keep an equivalent warn on `isError` (see Open Questions) — either is acceptable, but avoid silently swallowing errors differently from other hooks in this file (`useLiveHealthCheck`/`useReadyHealthCheck` don't log on error either, so dropping the warn to match that existing convention is reasonable).

## Data model
No backend/data model changes. Reuses existing generated type:
```
GetConfigurationResponse { version?: string; environment?: string; useMockAuth?: boolean; timestamp?: Date }
```
Local derived shape stays conceptually the same as today's `appInfo` state but computed inline from `configData` (or fallback) each render, no longer stored in `useState`.

## Interfaces
- No API surface changes — same endpoint (`GET /api/configuration`) already wired through `apiClient.configuration_GetConfiguration()` and `useConfigurationQuery`.
- UI: StatusBar footer — version tag, environment badge, branch badge (unchanged, unrelated to this fix), mock-auth badge, health-check dots (unchanged).

## Dependencies and scope
**In scope:** `frontend/src/components/StatusBar.tsx` only. `useConfigurationQuery`/`useConfiguration.ts` already exist and need no changes (verified: hook signature and `GetConfigurationResponse` fields — `version`, `environment`, `useMockAuth` — match what `StatusBar` currently reads off the untyped `backendConfig`).

**Out of scope:**
- The sibling `useManufacturingStockAnalysis` `as any` instance (issue #3730) — separate finding, not touched here.
- Any change to the generated API client, the `/api/configuration` backend endpoint, or `getAuthenticatedApiClient`.
- Visual/layout changes to the status bar beyond what's needed to keep current output identical.

## Rough plan
1. In `StatusBar.tsx`, import `useConfigurationQuery` from `../api/hooks/useConfiguration`; remove `getAuthenticatedApiClient` import.
2. Call `const { data: configData, isError } = useConfigurationQuery();` alongside the existing health-check hooks.
3. Replace the `appInfo` state/`useEffect` with derived values computed each render: prefer `configData` fields when present, else fall back to `getRuntimeConfig()` + `process.env.REACT_APP_VERSION` (mirroring today's two fallback branches — collapse the "API error" and "outer catch" fallback branches into one, since they're now redundant with the query's own error state).
4. Decide the loading-state behavior per Open Question 1 and implement (either keep `return null` until first data/fallback is ready, or render immediately with fallback and swap in real data when it arrives).
5. Remove now-unused `useState`/`useEffect` imports if no longer needed (health-check hooks don't use them directly in this file, so confirm before removing the import statement).
6. Run `npm run build` and `npm run lint` in `frontend/`.
7. Manually verify in a running app (or existing E2E/unit test if one covers StatusBar) that version/environment/mock-auth badges still render correctly against a live backend.

## Open questions
1. **Loading-state UX**: today the component renders nothing (`return null`) until the first fetch attempt resolves (backend or fallback). With `useConfigurationQuery`, `isLoading` is briefly true on mount. Default: keep `return null` while `isLoading && !configData`, so behavior is visually unchanged (brief blank footer on load, same as today). Flag if the reviewer prefers rendering fallback values immediately instead (removes the blank-footer flash, arguably an improvement, but changes existing behavior).
2. **Console warning on failure**: today logs `console.warn("Could not load configuration from backend API:", apiError)`. Default: drop it, since `useConfigurationQuery` already surfaces `isError`/`error` through TanStack Query devtools and no other hook in this file warns on error — keeping it would require wiring a `useEffect` on `isError` just to log, adding back complexity the refactor is meant to remove. Flag if the reviewer wants parity logging kept.
3. **No existing unit/E2E test targets `StatusBar` directly** (not verified against the test suite in this pass) — implementer should check `frontend/test/` for any StatusBar-specific test before assuming none exists, and add one if this component has no coverage today and the reviewer wants regression protection for the fallback path.
