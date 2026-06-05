# Specification: Refactor `useUserManagement.ts` to Use Generated Typed Client

## Summary
The `useGroupMembers` hook in `frontend/src/api/hooks/useUserManagement.ts` bypasses the generated typed API client and constructs a raw HTTP fetch call with `as any` casts and an incorrect `await` on a synchronous function. This specification covers replacing the manual fetch with the established pattern used by all other hooks in `frontend/src/api/hooks/`, restoring type safety, response deserialization, and pattern consistency.

## Background
The project uses an auto-generated TypeScript API client (`frontend/src/api/generated/api-client.ts`) produced from the backend's OpenAPI schema on each build. Every other hook in `frontend/src/api/hooks/` (e.g., `useOrgChart.ts`, `useArticles.ts`) follows a consistent pattern: call `getAuthenticatedApiClient()` synchronously, then invoke the generated method directly. The generated client handles base URL construction, authentication headers, and response deserialization via `<Response>.fromJS(...)`.

`useUserManagement.ts:9-27` diverges from this pattern for reasons that are not documented. The generated method `apiClient.userManagement_GetGroupMembers(groupId)` already exists at `api-client.ts:11643` and returns the correctly typed `GetGroupMembersResponse`. The current manual fetch:

1. Awaits a synchronous function (`getAuthenticatedApiClient()`), which is semantically misleading even though it works at runtime.
2. Uses two `as any` casts to reach into the client's private `baseUrl` and `http` fields, breaking type safety and creating a hidden coupling to the generated client's internal shape.
3. Calls `response.json()` directly, skipping `GetGroupMembersResponse.fromJS(...)`, which means future contract additions (dates, enums, nested objects) would not be coerced into the correct runtime types.

This is a small, isolated refactor with high signal: it aligns one hook with an established pattern, restores type safety, and removes drift.

## Functional Requirements

### FR-1: Replace manual fetch with generated client method
Rewrite the `queryFn` in `useGroupMembers` to call `apiClient.userManagement_GetGroupMembers(groupId)` instead of constructing a raw fetch.

**Acceptance criteria:**
- `useUserManagement.ts` no longer contains any `as any` casts.
- `useUserManagement.ts` no longer awaits `getAuthenticatedApiClient()` (the call is synchronous).
- `useUserManagement.ts` no longer manually constructs URLs, headers, or calls `response.json()`.
- The `queryFn` invokes `apiClient.userManagement_GetGroupMembers(groupId)` and returns its result.
- The function signature, return type (`GetGroupMembersResponse`), and the hook's public API are unchanged.

### FR-2: Preserve existing hook behavior
The refactor is a like-for-like replacement of the transport mechanism. Query key, enabled condition, caching behavior, and error propagation must remain identical from the caller's perspective.

**Acceptance criteria:**
- Query key remains `['userManagement', 'groupMembers', groupId]` (or whatever the current key is — preserve verbatim).
- The `enabled` predicate (e.g., `!!groupId`) is unchanged.
- Any other React Query options currently passed (staleTime, retry, etc.) are unchanged.
- Components consuming `useGroupMembers` require zero changes.

### FR-3: Match the pattern used elsewhere in `frontend/src/api/hooks/`
The final code shape should be visually consistent with peer hooks such as `useOrgChart.ts:13`.

**Acceptance criteria:**
- The hook structure mirrors the reference example in the brief:
  ```ts
  queryFn: async (): Promise<GetGroupMembersResponse> => {
    const apiClient = getAuthenticatedApiClient();
    return apiClient.userManagement_GetGroupMembers(groupId);
  },
  ```
- No import of fetch utilities, no manual URL building, no header construction.

## Non-Functional Requirements

### NFR-1: Type Safety
All `as any` casts in `useUserManagement.ts` must be eliminated. The file should pass `tsc --noEmit` with the project's existing strict settings and `npm run lint` with no new warnings.

### NFR-2: Behavioral Parity
Runtime behavior observed by callers must be identical: same response shape, same error handling semantics, same caching, same authentication flow. No regression in the UI or any consumer of `useGroupMembers`.

### NFR-3: Test Coverage
Any existing unit tests for `useUserManagement.ts` must continue to pass. If no tests currently exist for this hook, no new tests are required by this change (the brief describes a surgical refactor, not new functionality), but it should remain trivially testable via the same patterns used by other hooks.

### NFR-4: Validation
Before declaring done:
- `npm run build` succeeds.
- `npm run lint` passes with no new warnings.
- Any tests touching `useUserManagement` pass.

## Data Model
No data model changes. The response type `GetGroupMembersResponse` is already defined by the generated client and is unchanged.

## API / Interface Design

**Affected file:** `frontend/src/api/hooks/useUserManagement.ts` (lines 9-27)

**Before (current):**
```ts
queryFn: async (): Promise<GetGroupMembersResponse> => {
  const apiClient = await getAuthenticatedApiClient();
  const relativeUrl = `/api/UserManagement/group-members?groupId=${encodeURIComponent(groupId)}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
  });
  const data = await response.json();
  return data;
},
```

**After (target):**
```ts
queryFn: async (): Promise<GetGroupMembersResponse> => {
  const apiClient = getAuthenticatedApiClient();
  return apiClient.userManagement_GetGroupMembers(groupId);
},
```

**Backend endpoint:** No backend changes. The generated method `userManagement_GetGroupMembers` already calls `GET /api/UserManagement/group-members?groupId={groupId}` under the hood.

## Dependencies
- `frontend/src/api/generated/api-client.ts` — generated method `userManagement_GetGroupMembers` at line 11643 (already exists).
- `frontend/src/api/client.ts` (or equivalent) — `getAuthenticatedApiClient()` factory (already exists, returns synchronously).
- `@tanstack/react-query` — already in use by the hook.

No new dependencies. No version bumps. No regeneration of the API client required.

## Out of Scope
- Refactoring other hooks in `frontend/src/api/hooks/` (they already follow the correct pattern).
- Changes to the backend `UserManagement` controller, MediatR handlers, or DTOs.
- Regenerating the OpenAPI client or modifying generation configuration.
- Adding new unit tests for `useGroupMembers` beyond what already exists.
- Changes to consumers of `useGroupMembers` (none should be needed).
- Renaming, relocating, or restructuring `useUserManagement.ts`.
- Addressing any other findings or refactors in the UserManagement module that weren't called out in the brief.

## Open Questions
None.

## Status: COMPLETE