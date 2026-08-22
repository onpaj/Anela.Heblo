### task: write-tests

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`
- Reference (read-only, do not modify): `frontend/src/api/hooks/useLeaflet.ts`

- [ ] **Step 1: Write the failing test file**

Create `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` with the following content:

```ts
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useSubmitLeafletFeedbackMutation } from "../useLeaflet";
import * as clientModule from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { leaflet: ["leaflet"] },
}));

const mockGetClient = clientModule.getAuthenticatedApiClient as jest.MockedFunction<
  typeof clientModule.getAuthenticatedApiClient
>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const setFetch = (response: Partial<Response> & { json: () => Promise<unknown> }) => {
  const fetchMock = jest.fn().mockResolvedValue(response);
  mockGetClient.mockReturnValue({
    baseUrl: "http://test",
    http: { fetch: fetchMock },
  } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);
  return fetchMock;
};

const feedbackParams = {
  generationId: "gen-1",
  precisionScore: 4,
  styleScore: 5,
  comment: "looks good",
};

describe("useSubmitLeafletFeedbackMutation", () => {
  it("returns { success: false, alreadySubmitted: true } without throwing on HTTP 409", async () => {
    setFetch({ ok: false, status: 409, json: async () => ({}) });

    const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
      wrapper: createWrapper,
    });

    const res = await result.current.mutateAsync(feedbackParams);

    expect(res).toEqual({ success: false, alreadySubmitted: true });
  });

  it("throws with the status code in the message on a non-ok, non-409 response", async () => {
    setFetch({ ok: false, status: 500, json: async () => ({}) });

    const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
      wrapper: createWrapper,
    });

    await expect(result.current.mutateAsync(feedbackParams)).rejects.toThrow(
      "Submit feedback failed: 500",
    );
  });

  it("returns the parsed JSON body on an ok response", async () => {
    const body = { success: true, errorCode: null, alreadySubmitted: false };
    setFetch({ ok: true, json: async () => body });

    const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
      wrapper: createWrapper,
    });

    const res = await result.current.mutateAsync(feedbackParams);

    await waitFor(() => expect(res).toEqual(body));
  });
});
```

- [ ] **Step 2: Run the new test file to verify it passes**

```bash
cd frontend && npx jest src/api/hooks/__tests__/useLeaflet.test.ts --no-coverage 2>&1
```

Expected: all 3 tests pass (green). If a test fails, check the most likely causes:
- Import path errors: `useLeaflet.ts` and `client.ts` live at `frontend/src/api/hooks/useLeaflet.ts` and `frontend/src/api/client.ts` — the test file's relative imports (`../useLeaflet`, `../../client`) must resolve from `frontend/src/api/hooks/__tests__/`.
- `QUERY_KEYS.leaflet` missing from the mock: `useLeaflet.ts` builds `leafletKeys` via `[...QUERY_KEYS.leaflet]` at module load time — the mocked `QUERY_KEYS` object must include a `leaflet` array or the module throws on import.
- Rejection assertion mismatch: confirm the thrown message in source is exactly `` `Submit feedback failed: ${response.status}` `` (see `frontend/src/api/hooks/useLeaflet.ts`, `useSubmitLeafletFeedbackMutation`).

- [ ] **Step 3: Run the full frontend test suite to confirm no regressions**

```bash
cd frontend && npm test -- --watchAll=false 2>&1
```

Expected: all previously passing tests still pass; the new `useLeaflet.test.ts` suite is included and green.

- [ ] **Step 4: Run lint and build to satisfy repo validation gates**

```bash
cd frontend && npm run lint 2>&1 && npm run build 2>&1
```

Expected: no lint errors introduced by the new test file; build succeeds.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useLeaflet.test.ts
git commit -m "test: cover useSubmitLeafletFeedbackMutation's 409/error/ok branches

Adds unit tests for the mutationFn's three response-handling paths:
- HTTP 409 -> { success: false, alreadySubmitted: true } (no throw)
- non-ok, non-409 -> throws with status code in message
- ok -> resolves with the parsed JSON body

Closes the coverage gap flagged on frontend/src/api/hooks/useLeaflet.ts
(#3942). No production code changes."
```
