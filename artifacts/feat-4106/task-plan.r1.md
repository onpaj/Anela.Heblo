# Leaflet Generate Mutation React Query Hook Refactor - Implementation Plan

**Goal:** Add a `useGenerateLeafletMutation` React Query hook to `frontend/src/api/hooks/useLeaflet.ts` and refactor `LeafletGenerateTab.tsx` to use it instead of calling the generated API client and hand-rolling loading state inline, preserving all current behavior exactly.

**Architecture:** Pure frontend refactor, no backend/API contract change. The new hook's `mutationFn` calls `getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest(params))` directly (not a raw `http.fetch`, unlike the file's other hooks) so the generated client's typed-throw error branching (422 → `GenerateLeafletResponse`, 400 → `ProblemDetails`, other → `SwaggerException`) is preserved unchanged for the component's existing `instanceof GenerateLeafletResponse` error classification. No query invalidation is added (generation results aren't cached/read anywhere in the app).

**Tech Stack:** React, TypeScript, `@tanstack/react-query` (`useMutation`), Jest + React Testing Library, the NSwag-generated `frontend/src/api/generated/api-client.ts` client.

---

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

### task: refactor-leaflet-generate-tab-to-use-mutation-hook

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` (full file, currently 97 lines)
- Test: `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`

This task depends on `add-generate-leaflet-mutation-hook` being committed first (`useGenerateLeafletMutation` must already exist and be exported from `frontend/src/api/hooks/useLeaflet.ts`).

- [ ] **Step 1: Refactor the component to use the new hook**

Replace the entire contents of `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` with:

```tsx
import React, { useState } from 'react';
import LeafletForm from './LeafletForm';
import LeafletResult from './LeafletResult';
import { useGenerateLeafletMutation } from '../../api/hooks/useLeaflet';
import {
  AudienceType,
  ErrorCodes,
  GenerateLeafletResponse,
  LeafletLength,
} from '../../api/generated/api-client';

interface ErrorBanner {
  kind: 'insufficient' | 'transient';
  message: string;
}

const LeafletGenerateTab: React.FC = () => {
  const [topic, setTopic] = useState('');
  const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
  const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
  const [result, setResult] = useState('');
  const [generationId, setGenerationId] = useState<string | null>(null);
  const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);
  const generateLeaflet = useGenerateLeafletMutation();

  const generate = async () => {
    setGenerationId(null);
    setErrorBanner(null);
    try {
      const response = await generateLeaflet.mutateAsync({ topic, audience, length });
      setResult(response.content ?? '');
      setGenerationId(response.id ?? null);
    } catch (err: unknown) {
      if (err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval) {
        setErrorBanner({
          kind: 'insufficient',
          message: 'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
        });
      } else {
        setErrorBanner({
          kind: 'transient',
          message: 'Generování selhalo. Zkuste to prosím znovu.',
        });
      }
    }
  };

  return (
    <>
      {errorBanner && (
        <div
          role="alert"
          className={`mb-4 rounded p-3 text-sm ${
            errorBanner.kind === 'insufficient'
              ? 'bg-amber-100 text-amber-900 dark:bg-amber-900/30 dark:text-amber-300'
              : 'bg-red-100 text-red-900 dark:bg-red-900/30 dark:text-red-300'
          }`}
        >
          {errorBanner.message}
        </div>
      )}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div>
          <LeafletForm
            topic={topic}
            audience={audience}
            length={length}
            isLoading={generateLeaflet.isPending}
            onTopicChange={setTopic}
            onAudienceChange={setAudience}
            onLengthChange={setLength}
            onSubmit={generate}
          />
        </div>
        <div>
          {generateLeaflet.isPending ? (
            <div className="animate-pulse space-y-2">
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded w-3/4" />
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded" />
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded w-5/6" />
            </div>
          ) : (
            <LeafletResult content={result} generationId={generationId} onRegenerate={generate} />
          )}
        </div>
      </div>
    </>
  );
};

export default LeafletGenerateTab;
```

This removes the `getAuthenticatedApiClient` and `GenerateLeafletRequest` imports, removes the local `isLoading` `useState` and both `setIsLoading` calls (and the now-unneeded `finally` block), and replaces both `isLoading` read sites with `generateLeaflet.isPending`.

- [ ] **Step 2: Run the existing (not-yet-updated) test file to confirm it now fails**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`

Expected: FAIL. The test file's `jest.mock('../../../api/hooks/useLeaflet', () => ({ useSubmitLeafletFeedbackMutation: () => ({...}) }))` replaces the whole `useLeaflet` module, so the component's imported `useGenerateLeafletMutation` resolves to `undefined`. Calling it during render throws `TypeError: (0 , _useLeaflet.useGenerateLeafletMutation) is not a function`, and both test cases fail with this error.

- [ ] **Step 3: Update the test file's render wrapper and module mock**

Replace the entire contents of `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` with:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import LeafletGenerateTab from '../LeafletGenerateTab';
import { getAuthenticatedApiClient } from '../../../api/client';
import { ErrorCodes, GenerateLeafletResponse } from '../../../api/generated/api-client';

jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

jest.mock('../../../api/hooks/useLeaflet', () => ({
  ...jest.requireActual('../../../api/hooks/useLeaflet'),
  useSubmitLeafletFeedbackMutation: () => ({
    mutate: jest.fn(),
    isPending: false,
    isError: false,
  }),
}));

jest.mock('react-markdown', () => ({
  __esModule: true,
  default: ({ children }: { children: string }) => <div>{children}</div>,
}));

let mockGenerate: jest.Mock;

beforeEach(() => {
  jest.clearAllMocks();
  mockGenerate = jest.fn();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    leaflet_Generate: mockGenerate,
  });
});

function createWrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

async function fillAndSubmit() {
  fireEvent.change(screen.getByLabelText('Téma'), { target: { value: 'Bisabolol' } });
  fireEvent.click(screen.getByRole('button', { name: 'Vygenerovat leták' }));
}

describe('LeafletGenerateTab', () => {
  it('shows the insufficient knowledge banner when the API rejects with LeafletEmptyRetrieval', async () => {
    const errorResponse = new GenerateLeafletResponse({
      success: false,
      errorCode: ErrorCodes.LeafletEmptyRetrieval,
    });
    mockGenerate.mockRejectedValue(errorResponse);

    render(<LeafletGenerateTab />, { wrapper: createWrapper });
    await fillAndSubmit();

    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.');
    expect(banner.className).toContain('bg-amber-100');
  });

  it('shows the transient failure banner for a generic error', async () => {
    mockGenerate.mockRejectedValue(new Error('network down'));

    render(<LeafletGenerateTab />, { wrapper: createWrapper });
    await fillAndSubmit();

    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Generování selhalo. Zkuste to prosím znovu.');
    expect(banner.className).toContain('bg-red-100');
  });
});
```

The two test bodies and assertions are unchanged from the original file — only the added `QueryClientProvider` wrapper (`createWrapper`) and the `jest.requireActual`-based mock (which keeps the real `useGenerateLeafletMutation` while still stubbing `useSubmitLeafletFeedbackMutation` for the child `LeafletResult` component) are new.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`

Expected: PASS — both test cases pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx
git commit -m "$(cat <<'EOF'
Refactor LeafletGenerateTab to use useGenerateLeafletMutation

Replaces the inline getAuthenticatedApiClient().leaflet_Generate(...) call
and hand-rolled isLoading state with the new useGenerateLeafletMutation hook,
using mutateAsync inside the existing try/catch so the reset/error-classification
control flow is unchanged. Test file now wraps render in a QueryClientProvider
and requires the actual useLeaflet module so the real hook runs in tests.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GfeMm8RYfo3pHeyGsa52Dd
EOF
)"
```

---

### task: verify-leaflet-generate-refactor

**Files:**
- None (verification only — no source changes in this task)

This task has no code changes of its own; it confirms the two prior tasks satisfy the spec's FR-3 acceptance criteria (`npm run build` and `npm run lint` succeed with no new warnings/errors; all Leaflet-related Jest tests pass) before considering the feature complete. No backend files were touched by this feature (no `.cs`/`.csproj` changes), so `dotnet build`/`dotnet format` are not applicable here.

- [ ] **Step 1: Run the frontend production build**

Run: `cd frontend && npm run build`

Expected: PASS — build completes with `Compiled successfully.` (or the pre-existing warning baseline, unchanged by this feature) and no new TypeScript or webpack errors. In particular, no "Cannot find module" or type errors referencing `useGenerateLeafletMutation`, `GenerateLeafletParams`, `GenerateLeafletRequest`, or `GenerateLeafletResponse`.

- [ ] **Step 2: Run ESLint**

Run: `cd frontend && npm run lint`

Expected: PASS — exit code `0`, no new lint errors/warnings reported for `frontend/src/api/hooks/useLeaflet.ts`, `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`, `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`, or `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`.

- [ ] **Step 3: Run all Leaflet-related Jest tests together**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/api/hooks/__tests__/useLeaflet.test.ts src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx src/features/leaflet-generator/__tests__/LeafletGeneratorPage.test.tsx`

Expected: PASS — all test suites pass:
- `useLeaflet.test.ts`: 5 tests (3 `useSubmitLeafletFeedbackMutation` + 2 `useGenerateLeafletMutation`).
- `LeafletGenerateTab.test.tsx`: 2 tests.
- `LeafletGeneratorPage.test.tsx`: 8 tests (unaffected — it mocks `LeafletGenerateTab` entirely, so it exercises no code from this feature, but it imports the same module tree and must still pass).

- [ ] **Step 4: No commit for this task**

This task makes no file changes — it only runs verification commands against the commits made in `add-generate-leaflet-mutation-hook` and `refactor-leaflet-generate-tab-to-use-mutation-hook`. If any command in Steps 1-3 fails, fix the issue in the relevant prior task's files, re-commit there (do not amend), and re-run this task's steps from Step 1.
