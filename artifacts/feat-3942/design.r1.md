# Design: Test coverage for `useSubmitLeafletFeedbackMutation` 409 path

## Component Design

**New file:** `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`

Responsibility: unit-test the `mutationFn` of `useSubmitLeafletFeedbackMutation` (exported from `frontend/src/api/hooks/useLeaflet.ts`) in isolation, with `fetch` mocked via the module's `getAuthenticatedApiClient` dependency. No production code is touched.

Structure (mirrors `frontend/src/api/hooks/__tests__/useBoxFill.test.ts`):

- `jest.mock("../../client", ...)` stubbing `getAuthenticatedApiClient` and `QUERY_KEYS` (`{ leaflet: ["leaflet"] }`).
- `createWrapper` — a `QueryClientProvider` wrapper with `{ queries: { retry: false }, mutations: { retry: false } }`, used by `renderHook`.
- `setFetch(response)` helper — configures the mocked client's `http.fetch` to resolve with a given partial `Response`-like object, returns the `fetchMock` for call-site assertions if needed.
- `describe("useSubmitLeafletFeedbackMutation")` containing three `it` cases (409 → sentinel, non-409 error → throw, ok → parsed body), each rendering the hook with `renderHook(() => useSubmitLeafletFeedbackMutation(), { wrapper: createWrapper })` and driving it via `result.current.mutateAsync(params)`.

Interface under test (unchanged, for reference):
```ts
mutationFn: (params: {
  generationId: string;
  precisionScore: number;
  styleScore: number;
  comment?: string;
}) => Promise<SubmitLeafletFeedbackResult>
```

## Data Schemas

No schema changes. Test fixtures used as mock HTTP responses:

**409 (already submitted):**
```ts
{ ok: false, status: 409, json: async () => ({}) }
```
Expected resolved value: `{ success: false, alreadySubmitted: true }`

**Non-ok, non-409 (e.g. 500):**
```ts
{ ok: false, status: 500, json: async () => ({}) }
```
Expected: `mutateAsync` rejects with `Error("Submit feedback failed: 500")`

**Ok:**
```ts
{ ok: true, json: async () => ({ success: true, errorCode: null, alreadySubmitted: false }) }
```
Expected resolved value: the same object returned by `json()`.

Request params fixture used across all three cases (arbitrary, matches the hook's param type):
```ts
{ generationId: "gen-1", precisionScore: 4, styleScore: 5, comment: "looks good" }
```
