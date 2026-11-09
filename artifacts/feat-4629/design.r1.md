# Design: useBackgroundRefresh test coverage

## Component Design
- `useBackgroundRefresh.test.tsx` with `describe` blocks per hook: `useBackgroundTasks`, `useTaskHistory`, `useAllHistory`, `useTaskStatus`, `useForceRefreshTask`, `useRunHydrationTier`.
- Shared helpers: `createWrapper()` (QueryClient, retry false), `mockClient(fetchMock)` returning `{ baseUrl: 'http://api.test', http: { fetch } }` from `getAuthenticatedApiClient`, `okResponse(body)`, `errorResponse(status, jsonImpl)`.
- Production edit: in `forceRefreshTask`, `const error = await response.json().catch(() => ({}));`.

## Data Schemas
Fake response: `{ ok: boolean; status: number; json: () => Promise<unknown> }`. Non-JSON error: `json: () => Promise.reject(new SyntaxError('Unexpected token <'))`. Expected URLs: `http://api.test/api/backgroundrefresh/tasks`, `/tasks/{enc}/history?maxRecords=N`, `/history?maxRecords=N`, `/tasks/{enc}/status`, `/tasks/{enc}/force-refresh`, `/tiers/{n}/run`.
