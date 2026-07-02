## Module
Dashboard

## Finding
All six hooks in `frontend/src/api/hooks/useDashboard.ts` bypass the generated TypeScript `ApiClient` by casting to `any` and directly accessing internal properties:

```typescript
// Lines 38–41, 54–56, 70–72, 88–91, 113–115, 131–133 (same pattern repeated 6×)
const apiClient = getAuthenticatedApiClient(false);
const relativeUrl = `/api/dashboard/settings`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;  // ← internal property
const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' }); // ← internal transport
```

Two problems:
1. `(apiClient as any).baseUrl` — there is already a typed public helper `getApiBaseUrl()` exported from `frontend/src/api/client.ts` (line 178–181) that returns exactly this value.
2. `(apiClient as any).http.fetch(...)` — bypasses the generated typed methods entirely, reaching into the undocumented internal transport. If `ApiClient`'s internal structure changes, all six hooks silently break at runtime with no compile-time warning.

Additionally, `useAvailableTiles` unnecessarily `await`s the synchronous `getAuthenticatedApiClient()` call (line 37).

## Why it matters
- TypeScript's safety net is circumvented — refactors to `ApiClient` internals won't be caught by the compiler.
- `getApiBaseUrl()` already exists for this purpose; the hooks are re-deriving the same value the hard way.
- All six hooks share the same pattern, so any fix needs to happen consistently or it will recur.

## Suggested fix
Use `getApiBaseUrl()` for the base URL and call the generated typed client methods where they exist. If typed methods aren't generated for these endpoints yet, at minimum replace `(apiClient as any).baseUrl` with `getApiBaseUrl()`:

```typescript
import { getAuthenticatedApiClient, getApiBaseUrl } from '../client';

const apiClient = getAuthenticatedApiClient(false);  // no await needed
const fullUrl = `${getApiBaseUrl()}${relativeUrl}`;
const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
```

Longer-term: ensure Dashboard endpoints are covered by the generated OpenAPI client so the hooks can use the typed interface directly.

---
_Filed by daily arch-review routine on 2026-06-30._
