## Module
ExpeditionList

## Finding
All five query/mutation functions in `frontend/src/api/hooks/useExpeditionListArchive.ts` access internal implementation details of the generated API client by casting it to `any`:

```typescript
// lines 52, 82, 107, 135, 156 — accessing .baseUrl
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

// lines 58–64, 84–87, 109–113, 137–150 — calling internal HTTP client
const response = await (apiClient as any).http.fetch(fullUrl, { ... });
```

The `.http.fetch()` call bypasses the generated typed client entirely. The generated client wraps every endpoint with typed request/response shapes; calling `.http.fetch` directly means:
- TypeScript has no type-checked request/response contract for these endpoints.
- Any interceptor or middleware added to the generated client (e.g. auth token injection, retry logic) may or may not apply, depending on where in the client stack `.http` sits — this is opaque without reading the generator's output.
- If the generated client's internal field is renamed (e.g. from `.http` to `.httpClient`), all five hooks break at runtime with no compile error.

The `.baseUrl` cast is less severe — `docs/development/api-client-generation.md` and `CLAUDE.md` both describe the pattern `${apiClient.baseUrl}${relativeUrl}` — but `baseUrl` not being a public property forces the `as any` even for this sanctioned use.

## Why it matters
This is the only hook file in the codebase (that this review found) that uses `.http.fetch` directly. It represents a gap in API client generation coverage: these endpoints (`/dates`, `/{date}`, `/reprint`, `/run-fix`, `/download/{path}`) don't have generated typed methods, so someone hand-rolled HTTP calls against the raw client instead.

The `as any` pattern erodes the type safety guarantee that the generated client exists to provide.

## Suggested fix
Two complementary steps:

1. **Add the missing endpoints to the OpenAPI spec / generated client** so that `useExpeditionDates`, `useExpeditionListsByDate`, `useReprintExpeditionList`, and `useRunExpeditionListPrintFix` can call typed generated methods instead of `.http.fetch`. The `getExpeditionListDownloadUrl` helper can remain a manual URL helper since it just constructs a link, not an API call.

2. **Expose `baseUrl` publicly on the generated client** (or via a helper exported from `client.ts`) so the sanctioned `${apiClient.baseUrl}${relativeUrl}` pattern doesn't require an `as any` cast. This unblocks both this file and any future hook that needs to construct an absolute URL.

Until step 1 is done, the `as any` in `.http.fetch` should at minimum be replaced with a typed helper (`apiFetch(relativeUrl, init)`) that encapsulates the cast in one place rather than repeating it across every hook.

---
_Filed by daily arch-review routine on 2026-06-03._