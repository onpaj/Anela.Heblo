Changes committed. Here's the output summary:

---

# Implementation: Refactor `useUserManagement.ts` to Use Generated Typed Client

## What was implemented

Replaced the manual raw-fetch implementation inside `useResponsiblePersonsQuery`'s `queryFn` with a direct call to the generated typed client method `apiClient.userManagement_GetGroupMembers(groupId)`. Updated the companion test file to mock the new method shape instead of the old `baseUrl`/`http.fetch` internals.

## Files created/modified

- `frontend/src/api/hooks/useUserManagement.ts` — removed 13 lines of raw fetch logic (URL building, header construction, `as any` casts, `response.ok` check, `response.json()`) and replaced with 2-line generated client call
- `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — updated mock factory from `{ baseUrl, http: { fetch } }` to `{ userManagement_GetGroupMembers: mockGetGroupMembers }`, removed `global.fetch` mocking, added default `mockResolvedValue` in `beforeEach`

## Tests

- `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — 4 tests, all passing: loading state, initial state, hook structure, and cleanup

## How to verify

```bash
cd frontend
npm run build           # should compile cleanly
npx eslint src/api/hooks/useUserManagement.ts src/api/hooks/__tests__/useUserManagement.test.tsx  # no output = clean
npx react-scripts test --watchAll=false --testPathPattern="useUserManagement"  # 4 passed
```

## Notes

- All pre-existing lint errors in other test files are unrelated and pre-date this change; the two modified files are clean.
- The `node_modules` directory was installed via `npm install --legacy-peer-deps` because the worktree had no local install; `node_modules` is gitignored and not committed.
- The spec's "NFR-3: existing tests must continue to pass" was amended per the arch-review (Decision 4): the test file was updated rather than left broken, since the old mocks targeted implementation details that no longer exist.

## PR Summary

Replaces the drift in `useResponsiblePersonsQuery` where a manual raw `fetch` call with `as any` casts bypassed the generated TypeScript client. The hook now calls `apiClient.userManagement_GetGroupMembers(groupId)` — the same pattern used by every other hook in `frontend/src/api/hooks/`. This restores type safety, re-enters the `authenticatedHttp` pipeline for auth headers and 401 handling, and enables `GetGroupMembersResponse.fromJS` deserialisation.

### Changes
- `frontend/src/api/hooks/useUserManagement.ts` — removed URL building, `as any` casts, manual header construction, `response.ok` check, and `response.json()` call; replaced with 2-line generated client invocation
- `frontend/src/api/hooks/__tests__/useUserManagement.test.tsx` — migrated mock shape from `{ baseUrl, http.fetch }` to `{ userManagement_GetGroupMembers }` to match the refactored implementation

## Status
DONE