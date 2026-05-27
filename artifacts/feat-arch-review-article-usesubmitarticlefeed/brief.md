## Module
Article (frontend)

## Finding
`useSubmitArticleFeedbackMutation` in `useArticles.ts` bypasses the NSwag-generated API client entirely for the feedback submission request, instead reaching into the client's private `http` and `baseUrl` properties via two `as any` casts:

```typescript
// frontend/src/api/hooks/useArticles.ts:219-221
const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${articleId}/feedback`;
const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'POST',
    ...
});
```

A TODO comment (lines 218–220) acknowledges the issue with a 2026-05-25 arch-review tag but no corresponding GitHub issue has been filed to track it:
```typescript
// TODO(arch-review 2026-05-25): Uses private apiClient internals (baseUrl/http) via `as any`
// — same fragility as the hooks refactored in this PR. Keep raw fetch only for 409 branch;
// revisit when generated client exposes typed-mutation 409 handling.
```

The reason the bypass exists is to detect HTTP 409 (already-submitted feedback) as a typed result rather than an exception — a capability the generated client doesn't currently expose.

## Why it matters
- **Brittle against NSwag regeneration**: `baseUrl` and `http` are implementation details of the generated `AnelaHebloApiClient` class. A NSwag template change (e.g. switching the HTTP abstraction, renaming the field) regenerates the client and silently breaks the `as any` cast at runtime rather than at compile time.
- **No TypeScript type safety**: The `as any` escapes the type system entirely. The body shape (field names, types) is hand-rolled and will drift from the actual `SubmitArticleFeedbackRequest` C# type without any tooling warning.
- **Project rule violation**: Project rules (`CLAUDE.md`) require hooks to use `${apiClient.baseUrl}${relativeUrl}` — the URL is constructed correctly but via a private field, not a public API. The intent of the rule is to use the client's managed base URL, not to read private state.

## Suggested fix
The minimal fix is to construct the absolute URL using the generated client's **public** `baseUrl` accessor (if one exists or can be added to the NSwag template), and use the standard `fetch` with the application's authentication headers directly:

```typescript
// Option A — use the generated client's public baseUrl if available
const baseUrl = apiClient.baseUrl;  // only if NSwag exposes this as public
const fullUrl = `${baseUrl}/api/articles/${articleId}/feedback`;
const response = await fetch(fullUrl, { ... });
```

Or, if the generated client can't be changed, add a thin wrapper that exposes base URL publicly:
```typescript
// Option B — expose base URL from getAuthenticatedApiClient()
export const getApiBaseUrl = () => getAuthenticatedApiClient().baseUrl; // typed, not as any
```

Both options preserve the `if (response.status === 409)` branch that is the actual requirement, without depending on private internals.

A longer-term fix is to configure the NSwag template to return a typed result for 409 responses on this endpoint, eliminating the need for raw `fetch` entirely.

---
_Filed by daily arch-review routine on 2026-05-27._