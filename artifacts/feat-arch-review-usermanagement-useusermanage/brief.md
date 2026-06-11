## Module
UserManagement

## Finding
`frontend/src/api/hooks/useUserManagement.ts:9-27` manually constructs a raw HTTP fetch call instead of using the generated typed client method.

The generated client (`frontend/src/api/generated/api-client.ts:11643`) already exposes:
```ts
apiClient.userManagement_GetGroupMembers(groupId)
```

Instead, the hook does:
```ts
const apiClient = await getAuthenticatedApiClient();  // ← await on a sync function
const relativeUrl = `/api/UserManagement/group-members?groupId=${encodeURIComponent(groupId)}`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;  // ← as any
const response = await (apiClient as any).http.fetch(fullUrl, {  // ← as any
  method: 'GET',
  headers: { 'Content-Type': 'application/json' },
});
```

Three concrete problems:
1. `getAuthenticatedApiClient()` is synchronous; `await`-ing it is semantically wrong (works at runtime, signals a misunderstanding).
2. Two `as any` casts bypass TypeScript's type system to access private internals.
3. The raw `response.json()` call skips `GetGroupMembersResponse.fromJS(...)`, the generated deserialization / validation path.

Every other hook in `frontend/src/api/hooks/` (e.g. `useOrgChart.ts:13`, `useArticles.ts`) calls the generated method directly:
```ts
const apiClient = getAuthenticatedApiClient();
const response = await apiClient.userManagement_GetGroupMembers(groupId);
return response;
```

## Why it matters
- Drifts from the project's established hook pattern, making maintenance harder.
- `as any` silently breaks if the generated client's internal shape changes.
- Skipping `fromJS` means dates, enums, or nested objects would not be coerced to the right types if the contract ever grows.

## Suggested fix
Replace the manual fetch with the generated method:

```ts
queryFn: async (): Promise<GetGroupMembersResponse> => {
  const apiClient = getAuthenticatedApiClient();
  return apiClient.userManagement_GetGroupMembers(groupId);
},
```

The authentication, base-URL construction, and response parsing are all handled by the generated client and `getAuthenticatedApiClient()` already.

---
_Filed by daily arch-review routine on 2026-06-05._