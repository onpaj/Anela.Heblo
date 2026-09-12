### task: add-generate-leaflet-mutation-hook

**Files:**
- Modify: `frontend/src/api/hooks/useLeaflet.ts:1-2` (add import), `frontend/src/api/hooks/useLeaflet.ts:283-289` (insert new interface + hook between `useUploadLeafletDocumentMutation` and `useSubmitLeafletFeedbackMutation`)
- Test: `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`

- [ ] **Step 1: Write the failing test**

Edit `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`. Replace the top import block:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useSubmitLeafletFeedbackMutation } from "../useLeaflet";
import * as clientModule from "../../client";
```

with:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useGenerateLeafletMutation, useSubmitLeafletFeedbackMutation } from "../useLeaflet";
import * as clientModule from "../../client";
import { AudienceType, ErrorCodes, GenerateLeafletResponse, LeafletLength } from "../../generated/api-client";
```

Then append this new `describe` block at the end of the file (after the existing `describe("useSubmitLeafletFeedbackMutation", ...)` block's closing `});`):

```typescript
describe("useGenerateLeafletMutation", () => {
  const generateParams = {
    topic: "Bisabolol",
    audience: AudienceType.EndConsumer,
    length: LeafletLength.Medium,
  };

  it("resolves with the GenerateLeafletResponse returned by leaflet_Generate", async () => {
    const successResponse = new GenerateLeafletResponse({
      success: true,
      content: "Generated leaflet content",
      id: "gen-1",
      kbSourceCount: 3,
      leafletSourceCount: 2,
    });
    const mockLeafletGenerate = jest.fn().mockResolvedValue(successResponse);
    mockGetClient.mockReturnValue({
      leaflet_Generate: mockLeafletGenerate,
    } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);

    const { result } = renderHook(() => useGenerateLeafletMutation(), {
      wrapper: createWrapper,
    });

    const res = await result.current.mutateAsync(generateParams);

    expect(mockLeafletGenerate).toHaveBeenCalledTimes(1);
    expect(res).toBe(successResponse);
  });

  it("propagates a rejected GenerateLeafletResponse (422) unchanged out of mutateAsync", async () => {
    const errorResponse = new GenerateLeafletResponse({
      success: false,
      errorCode: ErrorCodes.LeafletEmptyRetrieval,
    });
    const mockLeafletGenerate = jest.fn().mockRejectedValue(errorResponse);
    mockGetClient.mockReturnValue({
      leaflet_Generate: mockLeafletGenerate,
    } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);

    const { result } = renderHook(() => useGenerateLeafletMutation(), {
      wrapper: createWrapper,
    });

    await expect(result.current.mutateAsync(generateParams)).rejects.toBe(errorResponse);
  });
});
```

(`mockGetClient` and `createWrapper` are the existing helpers already defined earlier in this file — no changes needed to them.)

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/api/hooks/__tests__/useLeaflet.test.ts`

Expected: FAIL — the new `describe("useGenerateLeafletMutation", ...)` block throws `TypeError: (0 , _useLeaflet.useGenerateLeafletMutation) is not a function` (or equivalent "is not a function" error), because `useGenerateLeafletMutation` is not yet exported from `frontend/src/api/hooks/useLeaflet.ts`. The pre-existing `useSubmitLeafletFeedbackMutation` tests still pass.

- [ ] **Step 3: Write minimal implementation**

Edit `frontend/src/api/hooks/useLeaflet.ts`. Replace the top import block:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
```

with:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  AudienceType,
  GenerateLeafletRequest,
  GenerateLeafletResponse,
  LeafletLength,
} from '../generated/api-client';
```

Then, between the end of `useUploadLeafletDocumentMutation` and the start of `useSubmitLeafletFeedbackMutation`, replace:

```typescript
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: leafletKeys.all });
    },
  });
};

/**
 * Submit precision/style feedback for a leaflet generation.
 * HTTP 409 is treated as already-submitted (not an error throw).
 */
export const useSubmitLeafletFeedbackMutation = () => {
```

with:

```typescript
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: leafletKeys.all });
    },
  });
};

export interface GenerateLeafletParams {
  topic: string;
  audience: AudienceType;
  length: LeafletLength;
}

/**
 * Generate a new leaflet from a topic/audience/length combination.
 * Calls the generated client method directly (not a raw http.fetch, unlike the
 * other hooks in this file) so leaflet_Generate's typed-throw behavior
 * (422 -> GenerateLeafletResponse, 400 -> ProblemDetails, other -> SwaggerException)
 * is preserved unchanged for callers.
 */
export const useGenerateLeafletMutation = () => {
  return useMutation({
    mutationFn: async (params: GenerateLeafletParams): Promise<GenerateLeafletResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.leaflet_Generate(new GenerateLeafletRequest(params));
    },
  });
};

/**
 * Submit precision/style feedback for a leaflet generation.
 * HTTP 409 is treated as already-submitted (not an error throw).
 */
export const useSubmitLeafletFeedbackMutation = () => {
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/api/hooks/__tests__/useLeaflet.test.ts`

Expected: PASS — all 5 tests in the file pass (3 existing `useSubmitLeafletFeedbackMutation` tests + 2 new `useGenerateLeafletMutation` tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useLeaflet.ts frontend/src/api/hooks/__tests__/useLeaflet.test.ts
git commit -m "$(cat <<'EOF'
Add useGenerateLeafletMutation hook to useLeaflet.ts

Wraps the existing leaflet_Generate generated-client call in a React Query
mutation, matching the one-hook-per-operation convention used by every other
Leaflet operation in this file. Calls the generated client method directly
(not a raw http.fetch) to preserve its typed-throw error branching.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GfeMm8RYfo3pHeyGsa52Dd
EOF
)"
```

---

