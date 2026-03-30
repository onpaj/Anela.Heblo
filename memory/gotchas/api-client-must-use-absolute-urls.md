# Gotcha: API Client Must Use Absolute URLs

**Problem:** Using relative URLs (e.g., `/api/catalog`) in frontend API hooks causes requests to hit the frontend dev server (port 3000) instead of the backend (port 5001).

**Root cause:** The generated API client has a `baseUrl` property. Relative URLs bypass it and go to the current browser origin, which is the Vite dev server in development.

**Fix:**
```typescript
// Wrong — hits wrong port:
const response = await (apiClient as any).http.fetch(`/api/catalog`, { method: 'GET' });

// Correct — uses baseUrl from client config:
const fullUrl = `${(apiClient as any).baseUrl}/api/catalog`;
const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
```

Always get the API client via `getAuthenticatedApiClient()` and use its `baseUrl`.
