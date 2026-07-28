# Design — StatusBar: replace `as any` config fetch with `useConfigurationQuery`

## Scope note
This is a refactor of an existing component's data-fetching internals, not a new feature. The rendered footer markup, classes, and layout are unchanged — only how `appInfo` is obtained changes (typed query hook instead of a manual `as any` fetch + local state). A minimal "rendering states" view is included below because the loading-state decision (Open Question 1 in the plan) directly affects what the user sees on mount; there is no new wireframe because none of the visible structure changes.

## Rendering states (unchanged layout, new data source)

```
State: LOADING (useConfigurationQuery().isLoading === true)
┌──────────────────────────────────────────────────────────┐
│ (StatusBar renders null — no footer bar, same as today)  │
└──────────────────────────────────────────────────────────┘

State: SUCCESS (configData present) — desktop
┌──────────────────────────────────────────────────────────┐
│ v1.4.2 | Production | API: heblo.anela.cz | ●Live ●Ready │
└──────────────────────────────────────────────────────────┘

State: SUCCESS — mobile
┌───────────────────────────────────────────┐
│ Anela Heblo v1.4.2  [Prod]        [Mock]   │
└───────────────────────────────────────────┘

State: ERROR / query rejected (configData undefined, isLoading false)
┌──────────────────────────────────────────────────────────┐
│ v0.1.0 | Development | API: localhost:5001 | ●Live ●Ready│
│         ^ same local-fallback values as today's catch     │
└──────────────────────────────────────────────────────────┘
```

Decision (resolves plan Open Question 1): keep the `return null` gate, but key it off `isLoading` only, not off a local `appInfo` state. Once the query has settled — success or error — a value is always derivable (real data or fallback), so there is no second blank frame after the fetch resolves, matching current behavior where the footer appears exactly once, fully formed.

## Component design

### Responsibility change
`StatusBar` currently owns three responsibilities: fetching config (network + parsing), deriving display fields, and rendering. After this change it owns only derivation and rendering; fetching/caching is delegated entirely to `useConfigurationQuery` (TanStack Query), which already exists and is otherwise unused.

### Interface boundary
- **Consumes:** `useConfigurationQuery(): UseQueryResult<GetConfigurationResponse>` from `frontend/src/api/hooks/useConfiguration.ts` — no changes to that file. Also `getRuntimeConfig()` (unchanged, still the source of `apiUrl` and the local-fallback `useMockAuth`/environment guess).
- **No longer consumes:** `getAuthenticatedApiClient` (removed import), `useState`/`useEffect` for this purpose (removed; not used elsewhere in the file, so both leave the `react` import entirely — `import React from "react";`).

### Control flow (replaces lines 19–24 and 39–90 of the current file)

```ts
const { data: configData, isLoading } = useConfigurationQuery();

if (isLoading) {
  return null;
}

const config = getRuntimeConfig();

const appInfo = {
  version: configData?.version || process.env.REACT_APP_VERSION || "0.1.0",
  environment:
    configData?.environment ?? (config.useMockAuth ? "Development" : "Production"),
  apiUrl: config.apiUrl,
  mockAuth: configData?.useMockAuth ?? config.useMockAuth,
};
```

Everything from `if (!appInfo) return null;` (current line 92) onward down to the end of the render is unchanged — it already only reads `appInfo.version` / `appInfo.environment` / `appInfo.apiUrl` / `appInfo.mockAuth`, which keep the same shape.

Notes on operator choice (implementer must preserve, not "clean up"):
- `version` uses `||`, not `??`, to preserve today's behavior of treating an empty-string version as absent (matches current `process.env.REACT_APP_VERSION || "0.1.0"`).
- `environment` and `mockAuth` use `??`, so an explicit `false` from `configData.useMockAuth` (real Azure AD in a non-mock environment) is respected and not overwritten by the fallback — this is a correctness requirement, not a style choice: `||` would be wrong here because `false` is a legitimate, meaningful value.

### Resolved open questions from the plan
1. **Loading-state UX** → keep `return null` during `isLoading`, as shown above. No visual regression; simplest change; the query's `retry: 1` and typical local/API latency mean this is a single short-lived frame, same order of magnitude as today's `useEffect`-triggered fetch.
2. **Console warning on failure** → drop it. `useLiveHealthCheck`/`useReadyHealthCheck`, the other two data hooks used in this same component, do not log on error either; adding a `useEffect` solely to log `isError` would reintroduce the exact complexity this refactor removes, for a component that already degrades gracefully to local fallback values. If the reviewer wants failure visibility, that belongs in a shared query-error-logging layer (e.g. a global `onError` in the `QueryClient` config), not per-hook — out of scope here.
3. **Test coverage** → no design change; carried forward as a note for the next step, not decided here.

## Data schemas
No backend or contract changes. Reuses the existing generated response type as-is:

```ts
// frontend/src/api/generated/api-client.ts (generated, unchanged)
class GetConfigurationResponse {
  version?: string;
  environment?: string;
  useMockAuth?: boolean;
  timestamp?: Date;
}
```

Derived local shape (not a `useState`, recomputed each render — see control flow above):

```ts
type AppInfo = {
  version: string;
  environment: string;
  apiUrl: string;
  mockAuth: boolean;
};
```

This is structurally identical to today's `appInfo` state type; only its source (derived value vs. `useState`) and population mechanism (inline expression vs. `useEffect` + manual fetch) change. No new fields, no renamed fields, no wire-format changes — `GET /api/configuration` is called exactly as before, now through `apiClient.configuration_GetConfiguration()` (typed) instead of `apiClient.http.fetch(...)` (untyped), which also means a non-200 response now surfaces as a thrown/rejected promise (`isError`) via the generated client's existing `throwException` path, rather than the old code's `if (response.ok)` check — functionally equivalent for this component since both paths land on the same local-fallback values.

## Dependencies and scope
Unchanged from the plan: single file, `frontend/src/components/StatusBar.tsx`. No changes to `useConfiguration.ts`, the generated client, or the backend endpoint.
