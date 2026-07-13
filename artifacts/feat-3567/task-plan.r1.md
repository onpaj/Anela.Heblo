# Fix onSuccess Invalidation Key in useEnqueueInvoiceImport Implementation Plan

**Goal:** Correct the React Query cache invalidation key in the `onSuccess` callback of `useEnqueueInvoiceImport` so that enqueuing an async invoice import actually invalidates (and refetches) the running-jobs query. Today the hand-written literal `[...QUERY_KEYS.invoices, "jobs"]` never prefix-matches the query registered under `[...QUERY_KEYS.invoices, "import", "jobs", "running"]`, making the intended immediate refresh a silent no-op. Replace the literal with the existing `invoiceImportQueryKeys.jobs()` factory method and add a regression test guarding against literal drift.

**Architecture:** Purely internal frontend fix. Single file changed (`frontend/src/api/hooks/useAsyncInvoiceImport.ts`), one new test file added (`frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts`). No backend, no component, no interface, no schema changes. React Query prefix-matching is the only integration point; both the invalidating mutation and the consuming query live in the same module. The `invoiceImportQueryKeys` factory the fix depends on already exists in the target file (lines 123-128).

**Tech Stack:** TypeScript, React, `@tanstack/react-query` (`useMutation`, `useQueryClient`, `invalidateQueries` prefix-matching), Jest + `@testing-library/react` (`renderHook`, `waitFor`, `QueryClientProvider`). Test files live under `frontend/src/api/hooks/__tests__/`.

---

### task: fix-invalidation-key-and-add-test

**Files:**
- Modify: `frontend/src/api/hooks/useAsyncInvoiceImport.ts:38-41` (the `onSuccess` body of `useEnqueueInvoiceImport`)
- Test: `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts` (new file)

**Context — current code (`frontend/src/api/hooks/useAsyncInvoiceImport.ts`):**

The mutation hook `useEnqueueInvoiceImport` currently ends with:

```typescript
    onSuccess: () => {
      // Invalidate running jobs queries to show the new job
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.invoices, "jobs"] });
    },
```

The literal `[...QUERY_KEYS.invoices, "jobs"]` produces `["invoices", "jobs"]`, but `useRunningInvoiceImportJobs` registers under `["invoices", "import", "jobs", "running"]`. The sequences diverge at the segment after `"invoices"` (`"jobs"` vs `"import"`), so React Query's prefix match never fires.

The module already exports the canonical factory (lines 123-128):

```typescript
export const invoiceImportQueryKeys = {
  all: () => [...QUERY_KEYS.invoices, "import"],
  jobs: () => [...QUERY_KEYS.invoices, "import", "jobs"] as const,
  jobStatus: (jobId: string) => [...QUERY_KEYS.invoices, "import", "jobs", "status", jobId] as const,
  runningJobs: () => [...QUERY_KEYS.invoices, "import", "jobs", "running"] as const,
};
```

`invoiceImportQueryKeys.jobs()` returns `["invoices", "import", "jobs"]`, which prefix-matches both the running-jobs query (`[..., "running"]`) and the per-job status query (`[..., "status", jobId]`). It is defined below the hook in module scope, but `onSuccess` is a runtime closure, so referencing it is safe (the const is initialized at module load, long before any mutation resolves). No import changes are needed — `invoiceImportQueryKeys` is in the same module and `QUERY_KEYS` stays imported (still used elsewhere in the file).

**Steps:**

- [ ] Step 1: Write the failing regression test. Create `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts` with exactly the following content. It renders `useEnqueueInvoiceImport` under a real `QueryClient`, spies on that client's `invalidateQueries`, resolves the mutation, and asserts the invalidation key equals `invoiceImportQueryKeys.jobs()` (and therefore prefix-matches `runningJobs()`). It also asserts the stale literal `["invoices", "jobs"]` is NOT used. This test fails against the current code because `onSuccess` passes `["invoices", "jobs"]`.

```typescript
import React from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useEnqueueInvoiceImport,
  invoiceImportQueryKeys,
} from '../useAsyncInvoiceImport';
import { getAuthenticatedApiClient } from '../../client';

// Mock the API client module used by the hook.
jest.mock('../../client', () => {
  const actual = jest.requireActual('../../client');
  return {
    ...actual,
    getAuthenticatedApiClient: jest.fn(),
  };
});

const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

const mockFetch = jest.fn();

describe('useEnqueueInvoiceImport onSuccess invalidation', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    jest.clearAllMocks();

    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    mockGetAuthenticatedApiClient.mockResolvedValue({
      baseUrl: 'http://api',
      http: { fetch: mockFetch },
    } as any);

    // Successful enqueue response.
    mockFetch.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ jobId: 'job-123', success: true }),
    });
  });

  const wrapper = ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it('invalidates using the invoiceImportQueryKeys.jobs() factory key', async () => {
    const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useEnqueueInvoiceImport(), { wrapper });

    result.current.mutate({} as any);

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    // The invalidation key must be the factory-derived jobs() key,
    // i.e. ["invoices", "import", "jobs"].
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: invoiceImportQueryKeys.jobs(),
    });

    // The factory key must prefix-match the running-jobs query key.
    const jobsKey = invoiceImportQueryKeys.jobs();
    const runningKey = invoiceImportQueryKeys.runningJobs();
    expect(runningKey.slice(0, jobsKey.length)).toEqual([...jobsKey]);

    // Guard against literal drift: the stale ["invoices", "jobs"] literal
    // must never be used.
    const usedKeys = invalidateSpy.mock.calls.map((call) => call[0]?.queryKey);
    expect(usedKeys).not.toContainEqual(['invoices', 'jobs']);
  });
});
```

- [ ] Step 2: Run the test to confirm it fails for the right reason. From the `frontend` directory run:

```bash
cd frontend && npm test -- useAsyncInvoiceImport --watchAll=false
```

Expected: the test fails at the `expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: invoiceImportQueryKeys.jobs() })` assertion, because the current `onSuccess` calls `invalidateQueries` with `["invoices", "jobs"]` instead of `["invoices", "import", "jobs"]`. Confirm the failure message shows the received key as `["invoices", "jobs"]`. If the test errors for an unrelated reason (module resolution, mock shape), fix the test before proceeding — do not touch the hook yet.

- [ ] Step 3: Apply the one-line fix. In `frontend/src/api/hooks/useAsyncInvoiceImport.ts`, replace the `onSuccess` body of `useEnqueueInvoiceImport` (lines 38-41). Change:

```typescript
    onSuccess: () => {
      // Invalidate running jobs queries to show the new job
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.invoices, "jobs"] });
    },
```

to:

```typescript
    onSuccess: () => {
      // Invalidate running jobs queries to show the new job
      queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.jobs() });
    },
```

Do not change anything else: the `mutationFn`, the URL, the request body, error handling, the other hooks, and the `invoiceImportQueryKeys` factory definition all stay exactly as they are. Do not remove the `QUERY_KEYS` import (it remains in use by the other queries and by the factory).

- [ ] Step 4: Run the test to confirm it passes.

```bash
cd frontend && npm test -- useAsyncInvoiceImport --watchAll=false
```

Expected: all assertions pass — the invalidation key now equals `invoiceImportQueryKeys.jobs()` (`["invoices", "import", "jobs"]`), it prefix-matches `runningJobs()`, and the stale `["invoices", "jobs"]` literal is absent.

- [ ] Step 5: Run lint and build to confirm no regressions.

```bash
cd frontend && npm run lint && npm run build
```

Expected: both succeed with no new errors or warnings attributable to the change. If the build's auto-generated OpenAPI client step runs, it should be unaffected (no API surface changed).

- [ ] Step 6: Commit the change.

```bash
cd /home/user/worktrees/feature-3567-Invoices-Onsuccess-Invalidation-Key-In-Useenqueuei && git add frontend/src/api/hooks/useAsyncInvoiceImport.ts frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts && git commit -m "Fix onSuccess invalidation key in useEnqueueInvoiceImport

Replace the hand-written literal [...QUERY_KEYS.invoices, \"jobs\"], which
never prefix-matched the running-jobs query key, with the canonical
invoiceImportQueryKeys.jobs() factory. Enqueuing an async invoice import
now correctly invalidates and refetches the running-jobs query instead of
relying solely on the 5s poll. Add a regression test guarding against
literal drift.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CHPDWDzWHTMettkeH5MZkK"
```

Expected: commit succeeds on the feature branch with exactly two files staged.
