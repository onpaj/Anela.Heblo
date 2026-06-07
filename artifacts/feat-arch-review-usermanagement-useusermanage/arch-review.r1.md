# Architecture Review: Refactor `useUserManagement.ts` to Use Generated Typed Client

## Skip Design: true

No UI components, screens, or visual design changes. This is a pure transport-layer refactor inside a single hook file. The consumer (`ResponsiblePersonCombobox`) is untouched.

## Architectural Fit Assessment

The feature is a textbook conformance fix: one hook drifted from a project-wide convention, and this work restores conformance. Verified facts on disk:

- **Pattern is universally established.** `useOrgChart.ts:13` and the rest of `frontend/src/api/hooks/` call `getAuthenticatedApiClient()` synchronously and invoke the generated method directly. `useUserManagement.ts:9-27` is the lone outlier.
- **`getAuthenticatedApiClient()` is synchronous** (`frontend/src/api/client.ts:275`, returns `ApiClient`, not `Promise<ApiClient>`). The current `await` is dead-but-harmless.
- **The generated method exists and already does the right things.** `api-client.ts:11660` builds the URL, `:11675` calls `this.http.fetch`, and `:11687` runs `GetGroupMembersResponse.fromJS(resultData200)`. It also throws on 400/401/non-2xx — meaning the manual `if (!response.ok) throw` in the current hook becomes redundant.
- **Centralised cross-cutting concerns are inside `authenticatedHttp.fetch`** (`client.ts:281-365`): auth headers, E2E cookies, 401 redirect, global toast handling, 409-business-outcome suppression. Routing the call through the generated client *re-enters* this pipeline; the current manual fetch *already* re-enters it (it grabs `(apiClient as any).http.fetch`). Net behavioural change for error UX: **none**.

Two corrections the spec did not catch:

1. **Identifier mismatch.** Brief and spec call the hook `useGroupMembers`. The actual export is `useResponsiblePersonsQuery` (`useUserManagement.ts:5`). Implementation must preserve the real name; renaming would break `ResponsiblePersonCombobox.tsx:11` and the test file. Spec FR-3's code sample mentions `useGroupMembers` and must be read as illustrative only.
2. **Existing tests are coupled to the old internals.** `__tests__/useUserManagement.test.tsx:7-17` mocks `getAuthenticatedApiClient` to return `{ baseUrl, http: { fetch } }` and stubs `global.fetch`. After the refactor, the mock shape must change to expose `userManagement_GetGroupMembers`. Spec NFR-3 ("existing unit tests must continue to pass") is **not** literally achievable — those tests *will* require an update. This needs to be acknowledged before work starts.

## Proposed Architecture

### Component Overview

```
ResponsiblePersonCombobox.tsx
        │  (uses)
        ▼
useResponsiblePersonsQuery  ◄────── React Query (cache, retry, enabled)
        │
        │  queryFn = () => apiClient.userManagement_GetGroupMembers(groupId)
        ▼
ApiClient (generated)                ◄── URL build, fromJS deserialization,
        │                                typed error branches (400/401)
        │  this.http.fetch(...)
        ▼
authenticatedHttp.fetch (in client.ts)
        │
        │  • buildAuthHeaders (token / E2E header)
        │  • credentials policy
        │  • 401 → clearTokenCache + globalAuthRedirectHandler
        │  • global toast (with 409 suppression)
        ▼
window.fetch
```

The shape after refactor matches every other hook in the directory. No new layers, no new abstractions.

### Key Design Decisions

#### Decision 1: Use the generated method directly; do not introduce a wrapper

**Options considered:**
- (a) Call `apiClient.userManagement_GetGroupMembers(groupId)` directly inside `queryFn` (peer-hook pattern).
- (b) Wrap the call in a thin module-private function for "future extensibility."
- (c) Introduce a `UserManagement` repository/service abstraction.

**Chosen approach:** (a).

**Rationale:** Every existing hook uses (a). (b) and (c) violate YAGNI and create a third pattern in a codebase that already has exactly one. The goal of this refactor is to *remove* drift, not introduce a new abstraction.

#### Decision 2: Drop the manual `if (!response.ok) throw` block

**Options considered:**
- (a) Remove it — the generated `processUserManagement_GetGroupMembers` already throws on non-2xx (`api-client.ts:11704-11707`).
- (b) Keep an equivalent check wrapping the generated call.

**Chosen approach:** (a).

**Rationale:** The generated client throws `SwaggerException` (or the typed `ProblemDetails` branches for 400/401) before the promise resolves. React Query will mark the query as `isError`. Keeping a redundant check would be dead code. This is consistent with `useOrgChart.ts`, which has no such guard.

#### Decision 3: Preserve `useResponsiblePersonsQuery` name, `queryKey` shape, and React Query options verbatim

**Options considered:**
- (a) Keep `queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId]`, `enabled: Boolean(groupId)`, `staleTime: 15 * 60 * 1000`, `retry: 2`, `retryDelay: 1000` exactly as today.
- (b) "Tidy" any of the above to match `useOrgChart`'s shape.

**Chosen approach:** (a).

**Rationale:** Spec FR-2 mandates behavioural parity. The cache key, retry policy, and staleTime are observable behaviour for consumers and any persisted React Query cache. Changing them is out of scope and would silently invalidate the cache on deploy.

#### Decision 4: Update the existing test file as part of this work

**Options considered:**
- (a) Update `__tests__/useUserManagement.test.tsx` so the `getAuthenticatedApiClient` mock returns `{ userManagement_GetGroupMembers: jest.fn() }` instead of `{ baseUrl, http }`.
- (b) Leave the test file alone and rely on NFR-3 as written.

**Chosen approach:** (a).

**Rationale:** Option (b) is impossible — the existing mock provides `baseUrl` and `http.fetch`; after the refactor the hook calls `userManagement_GetGroupMembers`, which is `undefined` on the current mock and will throw. The spec must be amended (see "Specification Amendments") and the tests must be migrated to the new mocking shape. This is a small, mechanical change confined to the test file.

## Implementation Guidance

### Directory / Module Structure

No new files. All changes are confined to:

- `frontend/src/api/hooks/useUserManagement.ts` — the hook itself.
- `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — mock shape and assertions.

No changes to `client.ts`, the generated client, `ResponsiblePersonCombobox.tsx`, or any peer hook.

### Interfaces and Contracts

**Hook export (unchanged signature):**
```ts
export const useResponsiblePersonsQuery = (groupId: string): UseQueryResult<GetGroupMembersResponse, Error>
```

**Implementation shape (target):**
```ts
import { useQuery } from '@tanstack/react-query';
import type { GetGroupMembersResponse } from '../generated/api-client';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export const useResponsiblePersonsQuery = (groupId: string) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.userManagement, 'group-members', groupId],
    enabled: Boolean(groupId),
    queryFn: async (): Promise<GetGroupMembersResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.userManagement_GetGroupMembers(groupId);
    },
    staleTime: 15 * 60 * 1000,
    retry: 2,
    retryDelay: 1000,
  });
};
```

Note: `apiClient.userManagement_GetGroupMembers` takes `string | undefined` (`api-client.ts:11660`). Passing `groupId: string` satisfies this; the `enabled` gate prevents calls when `groupId` is empty.

**Test mock shape (target):**
```ts
const mockUserManagement_GetGroupMembers = jest.fn();

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(() => ({
    userManagement_GetGroupMembers: mockUserManagement_GetGroupMembers,
  })),
  QUERY_KEYS: { userManagement: ['user-management'] },
}));
```

Tests should `mockResolvedValue({ success: true, members: [] })` directly on `mockUserManagement_GetGroupMembers`, not on `global.fetch`. Remove `global.fetch` mocking and `response.json` plumbing from the test file.

### Data Flow

For the single read path:

1. Consumer (`ResponsiblePersonCombobox`) renders with a `groupId`.
2. `useResponsiblePersonsQuery(groupId)` is called; React Query checks cache under `['user-management', 'group-members', groupId]`.
3. On cache miss and `Boolean(groupId) === true`, `queryFn` runs.
4. `getAuthenticatedApiClient()` returns a new `ApiClient` instance wired with `authenticatedHttp` (synchronous).
5. `apiClient.userManagement_GetGroupMembers(groupId)` builds `/api/UserManagement/group-members?groupId={...}` and calls `this.http.fetch`.
6. `authenticatedHttp.fetch` attaches auth/E2E headers, calls `window.fetch`, applies 401 redirect and toast logic.
7. Response is parsed via `GetGroupMembersResponse.fromJS(...)`. On 400/401, a `ProblemDetails` exception is thrown; on other non-2xx, `SwaggerException` is thrown.
8. The thrown error surfaces as `isError === true` in the consumer; the deserialized object surfaces as `data`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing tests fail because they mock the old internals. | Medium | Update the test file in the same PR. Spec amendment required (see below). |
| Subtle change in error object shape (`SwaggerException` vs. `Error("HTTP error! status: …")`). Any consumer doing `instanceof Error` checks or string-matching on the error message would break. | Low | Verified: `ResponsiblePersonCombobox.tsx` only reads `isError`/`isLoading`/`data` — never inspects the thrown error. No other consumers exist (`grep` confirmed). |
| Response shape change: today the hook returns raw JSON; after the refactor it returns a `GetGroupMembersResponse` *class instance* produced by `fromJS`. The consumer accesses `response?.members` (line 46) and calls `.map(member => member.displayName ?? '', ...)` — works on both. | Low | Spot-check on PR: confirm `GetGroupMembersResponse.members` is exposed as a public property on the generated class. Tested implicitly by build + the existing combobox at runtime. |
| `staleTime: 15 min` cache survives the deploy under the same key; if response shape diverges between deploys (raw JSON vs. class instance), already-cached entries could be plain objects without prototype methods. | Low | The consumer only uses property access (`.members`), no methods. Even if a stale plain-object entry exists, it still works. No mitigation required beyond awareness. |
| Someone "tidies" the staleTime/retry/queryKey while doing the refactor, silently changing cache behaviour. | Low | Spec FR-2 already forbids this. Reviewer should diff the React Query options block. |

## Specification Amendments

1. **Hook name correction (spec FR-1, FR-3, summary, background).** All references to `useGroupMembers` must be read as `useResponsiblePersonsQuery`. The hook will not be renamed.
2. **NFR-3 is unachievable as worded.** The existing test file (`frontend/src/api/hooks/__tests__/useUserManagement.test.tsx`) mocks the implementation details that this refactor removes. The acceptance criterion should be amended to: *"The existing test file is updated to mock `userManagement_GetGroupMembers` on the returned `ApiClient`. All tests under `useUserManagement.test.tsx` continue to pass after the update. No reduction in coverage of `useResponsiblePersonsQuery`."*
3. **Error-handling block removal (clarification, not a contradiction).** The current hook's `if (!response.ok) throw ...` block must be deleted — keeping it is impossible because the generated method throws before returning. This is implied by FR-1 but worth stating explicitly so the reviewer doesn't ask for it back.
4. **Out-of-scope clarification.** The `QUERY_KEYS.userManagement` constant in `client.ts:485` (value `["user-management"]`) is not touched, even though the query key spelling differs from the spec example (`'userManagement'`). Spec example wording is illustrative.

## Prerequisites

None. All required infrastructure already exists:

- `apiClient.userManagement_GetGroupMembers` is generated (`api-client.ts:11660`).
- `GetGroupMembersResponse` and its `fromJS` are generated.
- `getAuthenticatedApiClient()` is exported synchronously (`client.ts:275`).
- `QUERY_KEYS.userManagement` is defined (`client.ts:485`).

No migrations, no config, no client regeneration, no backend changes.