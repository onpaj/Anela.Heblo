# Architecture Review: Test coverage for `useSubmitLeafletFeedbackMutation` 409 path

## Skip Design: true
This is a test-only coverage-gap fix with no new UI, screens, or visual components. `useSubmitLeafletFeedbackMutation` and its consuming components already exist and are unchanged.

## Architectural Fit Assessment
This is a narrow, well-bounded addition that fits an existing, established pattern: React Query hook unit tests under `frontend/src/api/hooks/__tests__/`, one test file per hook module, mocking `getAuthenticatedApiClient` from `../../client` and asserting on `mutateAsync` results. No new integration points are introduced. Confirmed against `frontend/src/api/hooks/__tests__/useBoxFill.test.ts`, which tests sibling mutations (`useAddBoxItem`, `useRemoveBoxItem`, etc.) with the exact same mock-fetch-and-assert-on-mutateAsync shape needed here, including a case that asserts a non-2xx JSON body is still returned unthrown (`useAddBoxItem surfaces a failure body returned with HTTP 400`) — directly analogous to the 409 case in scope.

## Proposed Architecture

### Component Overview
```
frontend/src/api/hooks/useLeaflet.ts                          (unchanged, production code)
  └── useSubmitLeafletFeedbackMutation()                      (unit under test)

frontend/src/api/hooks/__tests__/useLeaflet.test.ts            (new test file)
  ├── mocks ../../client (getAuthenticatedApiClient)
  ├── createWrapper (QueryClientProvider, retry: false)
  └── describe("useSubmitLeafletFeedbackMutation")
        ├── it 409 -> { success: false, alreadySubmitted: true }, no throw
        ├── it non-ok/non-409 (e.g. 500) -> rejects with Error "Submit feedback failed: 500"
        └── it ok -> resolves with parsed JSON body
```

### Key Design Decisions

#### Decision 1: New dedicated test file vs. extending an existing one
**Options considered:** (a) create `useLeaflet.test.ts` as a new file; (b) there is no existing `useLeaflet.test.ts` to extend today.
**Chosen approach:** Create `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`.
**Rationale:** No test file currently exists for this hook module (confirmed: `find frontend/src/api/hooks/__tests__ -iname "*leaflet*"` returns nothing). One-file-per-hook-module is the established convention across the directory (24+ existing files each map 1:1 to a hook module).

#### Decision 2: Test only `mutationFn` behavior via `mutateAsync`, not render-state transitions
**Options considered:** (a) assert on `result.current.isSuccess`/`isError`/`data` after `waitFor`; (b) call `mutateAsync` directly and assert on its resolved value or thrown rejection.
**Chosen approach:** (b), matching `useBoxFill.test.ts`'s dominant pattern (`await result.current.mutateAsync(...)`).
**Rationale:** Simpler, faster, and the issue's suggested approach explicitly frames this as testing `mutationFn` directly. `mutateAsync` surfaces the exact same resolve/reject semantics as `mutationFn` since no `onError`/`onSuccess` callback exists on `useSubmitLeafletFeedbackMutation` to alter the outcome.

## Implementation Guidance

### Directory / Module Structure
- New file only: `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`.
- No changes to `frontend/src/api/hooks/useLeaflet.ts` or any other production file.

### Interfaces and Contracts
- Mock shape (copy the pattern from `useBoxFill.test.ts`):
  ```ts
  jest.mock("../../client", () => ({
    getAuthenticatedApiClient: jest.fn(),
    QUERY_KEYS: { leaflet: ["leaflet"] },
  }));
  ```
  (`QUERY_KEYS.leaflet` is only needed if any query hook query keys are exercised; for the mutation-only scope here it can be a minimal stub — confirm by running the test, add other `QUERY_KEYS` entries only if a different exported hook is imported transitively and needs them.)
- `setFetch` helper mocking `{ baseUrl, http: { fetch: jest.fn() } }` exactly as in `useBoxFill.test.ts`.
- Response mocks:
  - 409 case: `{ ok: false, status: 409, json: async () => ({}) }` (json need not be called on this branch, but include it defensively since the mock type expects it).
  - non-409 error case: `{ ok: false, status: 500, json: async () => ({}) }`.
  - ok case: `{ ok: true, json: async () => ({ success: true, errorCode: null, alreadySubmitted: false }) }`.
- Assert rejection via `await expect(result.current.mutateAsync(params)).rejects.toThrow('Submit feedback failed: 500')` (or `.rejects.toThrow(/500/)`).

### Data Flow
Test → mocked `getAuthenticatedApiClient()` → mocked `http.fetch` returns a canned `Response`-shaped object → `mutationFn` branches on `response.status` / `response.ok` exactly as in current source → resolved/rejected value asserted directly, no real network or backend involved.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Test asserts on the literal error message string, which is brittle to unrelated wording changes | Low | Acceptable per NFR-1 in spec; use `toThrow(/500/)` or exact match per existing sibling-file convention (sibling files use loose assertions elsewhere but exact status-code matches are fine here since the source string is a fixed template literal) |
| New test file forgets to mock `QUERY_KEYS.leaflet` and breaks at import time if other hooks in the same module reference it during module evaluation | Low | `leafletKeys` in `useLeaflet.ts` is evaluated at module load via `QUERY_KEYS.leaflet` spread — the jest mock must supply a `leaflet` array on `QUERY_KEYS`, matching the `useBoxFill.test.ts` mock pattern which supplies each key module needs |

## Specification Amendments
None. The spec (`spec.r1.md`) is implementable as written; this review only pins down the concrete file path and mock shape.

## Prerequisites
None — no new dependencies, config, or infrastructure needed. Existing Jest/RTL/React Query test tooling already in place.
