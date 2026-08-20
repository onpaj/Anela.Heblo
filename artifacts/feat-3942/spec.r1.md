# Specification: Test coverage for `useSubmitLeafletFeedbackMutation` 409 path

## Summary
`frontend/src/api/hooks/useLeaflet.ts` exposes `useSubmitLeafletFeedbackMutation`, whose `mutationFn` has a special-cased HTTP 409 branch (returns `{ success: false, alreadySubmitted: true }` instead of throwing) that is currently untested. This is a test-only change: add unit tests covering the mutation's three response-handling branches. No production code changes.

## Background
Weekly coverage-gap tooling flagged `useLeaflet.ts` at 8.3% line coverage (threshold 60%). The riskiest untested branch is the 409-as-sentinel-value contract in `useSubmitLeafletFeedbackMutation`: if the `response.status === 409` check regresses (e.g. removed, or changed to check `response.ok` first), a duplicate feedback submission would throw and surface as an error toast to the operator instead of a quiet "already submitted" state. The non-409 error-throw path is also untested.

## Functional Requirements

### FR-1: Test the 409 already-submitted path
Add a unit test that mocks `fetch` (via the existing `getAuthenticatedApiClient` mock pattern used elsewhere in `frontend/src/api/hooks/__tests__/`) to return `{ ok: false, status: 409 }` and asserts `mutateAsync` resolves (does not throw) with `{ success: false, alreadySubmitted: true }`.
**Acceptance criteria:**
- Test calls `useSubmitLeafletFeedbackMutation().mutateAsync(...)` with a representative params object.
- Test asserts the resolved value strictly equals `{ success: false, alreadySubmitted: true }`.
- Test asserts no exception is thrown / the promise does not reject.

### FR-2: Test the non-ok, non-409 error path
Add a unit test that mocks `fetch` to return `{ ok: false, status: 500 }` (or another non-409 error status) and asserts `mutateAsync` rejects with an `Error` whose message contains the status code.
**Acceptance criteria:**
- Test asserts the promise rejects.
- Test asserts the rejection is an `Error` with message matching `Submit feedback failed: 500` (per the existing `throw new Error(\`Submit feedback failed: ${response.status}\`)` in source).

### FR-3: Test the ok path
Add a unit test that mocks `fetch` to return `{ ok: true }` with a `json()` resolving to a representative `SubmitLeafletFeedbackResult` body, and asserts `mutateAsync` resolves with that parsed body.
**Acceptance criteria:**
- Test asserts the resolved value equals the mocked JSON body (e.g. `{ success: true }`).

## Non-Functional Requirements

### NFR-1: Consistency with existing test conventions
Tests must follow the established pattern used by sibling hook test files (e.g. `frontend/src/api/hooks/__tests__/useBoxFill.test.ts`): mock `../../client`'s `getAuthenticatedApiClient` to return `{ baseUrl, http: { fetch: jest.fn() } }`, wrap the hook in a `QueryClientProvider` test wrapper with `retry: false`, and call `mutateAsync` directly rather than asserting on hook render state transitions.

### NFR-2: No production code changes
This is a coverage-gap fix. `useLeaflet.ts` itself must not be modified — only a new/extended test file.

## Data Model
No data model changes. Relevant existing type: `SubmitLeafletFeedbackResult { success: boolean; errorCode?: string | null; alreadySubmitted?: boolean }` (already defined in `useLeaflet.ts`).

## API / Interface Design
No API changes. Tests exercise the existing `mutationFn` of `useSubmitLeafletFeedbackMutation` (`POST {baseUrl}/api/leaflet/feedback`) purely through mocked `fetch` responses — no real network calls.

## Dependencies
- `@testing-library/react` (`renderHook`, `waitFor`) — already a dev dependency, used by sibling tests.
- `@tanstack/react-query` (`QueryClient`, `QueryClientProvider`) — already used by sibling tests.
- Jest — existing test runner/config, no changes needed.

## Out of Scope
- Testing the other `useLeaflet.ts` hooks (queries, upload, delete, feedback list) — only `useSubmitLeafletFeedbackMutation` is in scope per the issue.
- Any change to `useLeaflet.ts` production code.
- E2E/integration tests — this is unit-level only.

## Open Questions
None.

## Status: COMPLETE
