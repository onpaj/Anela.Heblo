# Fix Article Feedback List Sort-Direction Parameter Mismatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the frontend `descending` field/query-string key to `sortDescending` so the article feedback list endpoint actually honors the user's sort-direction toggle.

**Architecture:** Pure frontend rename of one params field, one `URLSearchParams.append` key, and one adapter pass-through. Backend `[FromQuery] bool sortDescending = true` is the contract and stays unchanged. Sibling feedback adapters (`useKbFeedbackAdapter`, `useLeafletFeedbackAdapter`) already use the correct name and serve as the reference shape.

**Tech Stack:** TypeScript, React, React Query (`@tanstack/react-query`), Jest + `@testing-library/react`.

---

## File Structure

This is a surgical contract-alignment fix. No new files, no restructuring.

| File | Responsibility | Change |
|------|----------------|--------|
| `frontend/src/api/hooks/useArticles.ts` | Defines `ArticleFeedbackListParams` and `useArticleFeedbackListQuery`. Builds the `?sortDescending=...` query string. | Modify (rename field, fix query key) |
| `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts` | Translates `GenericFeedbackParams` → `ArticleFeedbackListParams`. | Modify (pass-through `sortDescending` instead of renaming to `descending`) |
| `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts` | Asserts adapter-to-hook parameter translation. | Modify (`descending: true` → `sortDescending: true`) |
| `frontend/src/api/hooks/__tests__/useArticles.test.ts` | Hook-level regression test for `useArticleFeedbackListQuery`. | **Create** (new file) — assert URL contains `sortDescending=...` and not `descending=...` |

**Out of scope (do not touch):**
- `backend/.../ArticlesController.cs` (backend contract is source of truth)
- `frontend/src/components/feedback/types.ts` (`GenericFeedbackParams.sortDescending` already correct)
- `useKbFeedbackAdapter.ts`, `useLeafletFeedbackAdapter.ts` (already pass through correctly)

---

## Task 1: Add hook-level regression test for the wire contract (TDD red)

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useArticles.test.ts`

This test intercepts the HTTP layer and asserts on the URL the hook constructs. It must fail against the current code (which appends `descending=...`) before any production change is made. Per the arch review (Risk row 2), mocking `useArticleFeedbackListQuery` itself does not verify the wire contract — we must mock `apiClient.http.fetch` and inspect the URL string.

- [ ] **Step 1: Write the failing test file**

Create `frontend/src/api/hooks/__tests__/useArticles.test.ts` with the following content. The pattern (mock `getAuthenticatedApiClient`, intercept `mockHttp.fetch`, assert on the URL substring) mirrors `useKnowledgeBase.test.ts` in the same folder.

```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useArticleFeedbackListQuery } from '../useArticles';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    articles: ['articles'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const mockFetchResponse = (data: unknown, ok = true) => ({
  ok,
  json: jest.fn().mockResolvedValue(data),
  status: ok ? 200 : 500,
});

const emptyFeedbackListResponse = {
  articles: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  totalPages: 0,
  stats: {
    totalArticles: 0,
    totalWithFeedback: 0,
    avgPrecisionScore: null,
    avgStyleScore: null,
  },
};

describe('useArticleFeedbackListQuery', () => {
  let mockHttp: { fetch: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockHttp = { fetch: jest.fn() };
    mockGetAuthenticatedApiClient.mockReturnValue({
      baseUrl: 'http://localhost:5001',
      http: mockHttp,
    } as any);
  });

  it('appends sortDescending=true to the query string (not descending=...)', async () => {
    mockHttp.fetch.mockResolvedValue(mockFetchResponse(emptyFeedbackListResponse));

    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: true }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockHttp.fetch.mock.calls[0][0];
    expect(calledUrl).toContain('sortDescending=true');
    expect(calledUrl).not.toContain('descending=true');
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });

  it('appends sortDescending=false when toggled off', async () => {
    mockHttp.fetch.mockResolvedValue(mockFetchResponse(emptyFeedbackListResponse));

    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: false }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockHttp.fetch.mock.calls[0][0];
    expect(calledUrl).toContain('sortDescending=false');
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });

  it('omits sortDescending from the URL when undefined (backend default applies)', async () => {
    mockHttp.fetch.mockResolvedValue(mockFetchResponse(emptyFeedbackListResponse));

    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortBy: 'CreatedAt' }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockHttp.fetch.mock.calls[0][0];
    expect(calledUrl).not.toMatch(/[?&]sortDescending=/);
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });

  it('builds URL with all filter params including sortDescending', async () => {
    mockHttp.fetch.mockResolvedValue(mockFetchResponse(emptyFeedbackListResponse));

    const { result } = renderHook(
      () =>
        useArticleFeedbackListQuery({
          hasFeedback: true,
          requestedBy: 'user@anela.cz',
          sortBy: 'CreatedAt',
          sortDescending: false,
          page: 2,
          pageSize: 10,
        }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const calledUrl: string = mockHttp.fetch.mock.calls[0][0];
    expect(calledUrl).toContain('hasFeedback=true');
    expect(calledUrl).toContain('requestedBy=user%40anela.cz');
    expect(calledUrl).toContain('sortBy=CreatedAt');
    expect(calledUrl).toContain('sortDescending=false');
    expect(calledUrl).toContain('page=2');
    expect(calledUrl).toContain('pageSize=10');
    expect(calledUrl).not.toMatch(/[?&]descending=/);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useArticles.test.ts --no-coverage`

Expected: The TypeScript compiler fails on `{ sortDescending: true }` because `ArticleFeedbackListParams` still has `descending?: boolean`, not `sortDescending?: boolean`. If the type field were renamed in isolation, the URL assertions (`expect(calledUrl).toContain('sortDescending=true')`) would still fail because the production code still calls `searchParams.append('descending', ...)`.

Either failure mode is the expected RED — the test is guarding the contract from both sides.

- [ ] **Step 3: Commit the failing test**

```bash
git add frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "test: add regression test for article feedback list sortDescending query key"
```

---

## Task 2: Rename the params field and the query string key in `useArticles.ts`

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts:73` (field rename)
- Modify: `frontend/src/api/hooks/useArticles.ts:267-268` (query key rename)

This is the core fix. Two edits in the same file.

- [ ] **Step 1: Rename the field on `ArticleFeedbackListParams`**

In `frontend/src/api/hooks/useArticles.ts`, change the interface definition from:

```typescript
export interface ArticleFeedbackListParams {
  hasFeedback?: boolean;
  requestedBy?: string;
  sortBy?: string;
  descending?: boolean;
  page?: number;
  pageSize?: number;
}
```

to:

```typescript
export interface ArticleFeedbackListParams {
  hasFeedback?: boolean;
  requestedBy?: string;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}
```

- [ ] **Step 2: Update the query-string builder to use the new key**

In the same file, in the `useArticleFeedbackListQuery` function body, change:

```typescript
      if (params.descending !== undefined)
        searchParams.append('descending', params.descending.toString());
```

to:

```typescript
      if (params.sortDescending !== undefined)
        searchParams.append('sortDescending', params.sortDescending.toString());
```

- [ ] **Step 3: Confirm there are no remaining `descending` references in this file**

Run: `cd frontend && grep -n "descending" src/api/hooks/useArticles.ts`

Expected: no matches. If anything still appears, fix it before continuing.

- [ ] **Step 4: Re-run the hook regression test (should now partially pass)**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useArticles.test.ts --no-coverage`

Expected: PASS — all four cases. The TypeScript field rename allows the test to compile, and the production code now appends `sortDescending=...`, satisfying the URL assertions.

If the test still fails, do not move on. Re-read the diff against the test expectations.

- [ ] **Step 5: Do NOT commit yet — the adapter and its test still reference `descending`**

The codebase will not compile (`useArticleFeedbackAdapter.ts` passes `descending: params.sortDescending` against the now-renamed field, and the adapter test asserts `descending: true`). Both must be fixed together in Task 3 to land a green commit.

---

## Task 3: Update the adapter and its test in lockstep

**Files:**
- Modify: `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts:10`
- Modify: `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts:103`

Per arch review Specification Amendment 1, the article adapter's rename line becomes a no-op pass-through (`sortDescending: params.sortDescending`). After this task, the adapter's call shape matches its siblings.

- [ ] **Step 1: Update the adapter pass-through**

In `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts`, change line 10 from:

```typescript
    descending: params.sortDescending,
```

to:

```typescript
    sortDescending: params.sortDescending,
```

The full block after change:

```typescript
export function useArticleFeedbackAdapter(params: GenericFeedbackParams) {
  const query = useArticleFeedbackListQuery({
    page: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    requestedBy: params.userId,
  });
  // ...rest unchanged
```

- [ ] **Step 2: Update the adapter test assertion**

In `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts`, change the assertion at line 103 from:

```typescript
test('translates GenericFeedbackParams to article-specific params', () => {
  renderHook(() => useArticleFeedbackAdapter(params));
  expect(articleHooks.useArticleFeedbackListQuery).toHaveBeenCalledWith(
    expect.objectContaining({
      page: 1,
      pageSize: 20,
      sortBy: 'CreatedAt',
      descending: true,
      requestedBy: 'user@anela.cz',
    }),
  );
});
```

to:

```typescript
test('translates GenericFeedbackParams to article-specific params', () => {
  renderHook(() => useArticleFeedbackAdapter(params));
  expect(articleHooks.useArticleFeedbackListQuery).toHaveBeenCalledWith(
    expect.objectContaining({
      page: 1,
      pageSize: 20,
      sortBy: 'CreatedAt',
      sortDescending: true,
      requestedBy: 'user@anela.cz',
    }),
  );
});
```

- [ ] **Step 3: Confirm no remaining `descending` references in the article-feedback files**

Run: `cd frontend && grep -rn "descending" src/api/hooks/useArticles.ts src/api/hooks/__tests__/useArticles.test.ts src/components/feedback/adapters/useArticleFeedbackAdapter.ts src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts`

Expected: no matches across all four files. Other `descending` mentions elsewhere in the codebase (comments in `useInventory`, unrelated catalog tests) are not part of this fix and must remain untouched.

- [ ] **Step 4: Run all touched test files**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useArticles.test.ts src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts --no-coverage`

Expected: all tests PASS. Both the hook regression suite and the adapter suite are green.

- [ ] **Step 5: Commit the production fix and its dependent test update**

```bash
git add frontend/src/api/hooks/useArticles.ts \
        frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts \
        frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts
git commit -m "fix: send sortDescending query key for article feedback list"
```

---

## Task 4: Verify the wider build, lint, and test surface

The rename is targeted, but the field name is exported on `ArticleFeedbackListParams`. Any caller outside the files already touched (none expected) would surface here as a compile error.

- [ ] **Step 1: Run the full frontend type-check / build**

Run: `cd frontend && npm run build`

Expected: build succeeds with no errors. If a reference to `.descending` on `ArticleFeedbackListParams` survives anywhere, the TypeScript compiler will catch it here.

- [ ] **Step 2: Run the frontend linter**

Run: `cd frontend && npm run lint`

Expected: no new warnings or errors related to the rename. Pre-existing lint output that was present on `main` is acceptable.

- [ ] **Step 3: Run the broader Jest suite for any indirect coverage**

Run: `cd frontend && npx jest src/api/hooks src/components/feedback --no-coverage`

Expected: all tests PASS. This covers all hook tests and all feedback adapter/component tests in one go — catches any consumer of `useArticleFeedbackListQuery` or `useArticleFeedbackAdapter` that we may have missed.

- [ ] **Step 4: Final sanity grep for the old key across the article feedback paths**

Run: `cd frontend && grep -n "'descending'" src/api/hooks/useArticles.ts src/components/feedback/adapters/useArticleFeedbackAdapter.ts`

Expected: no matches. The wire key `'descending'` should not appear in either file.

- [ ] **Step 5: No commit needed if no changes were made**

If any fix was required in this verification step, commit it as a follow-up:

```bash
git add -p
git commit -m "fix: clean up remaining descending references"
```

If verification passed cleanly, this task produces no commit.

---

## Spec Coverage Self-Check

| Spec requirement | Task |
|------------------|------|
| FR-1: Frontend sends `sortDescending=true/false` | Task 2 (production fix) |
| FR-2: Internal field renamed to `sortDescending`, no `descending` references survive | Task 2 (interface) + Task 3 (adapter + test) + Task 4 (build/lint/grep) |
| FR-3: UI toggle behavior produces visibly reordered list, default descending preserved | Implicitly verified — the toggle UI already passes `sortDescending` via `GenericFeedbackParams`; once the adapter forwards it correctly (Task 3) and the hook serializes it correctly (Task 2), the end-to-end flow works. Confirmed by Task 1's third test case (`undefined → URL omits key → backend default `true` applies`). |
| FR-4: Regression test guards against parameter-name drift | Task 1 (creates the test); Task 2 step 4 (verifies it passes after fix) |
| NFR-1 (no perf impact), NFR-2 (no security impact), NFR-3 (no compat shim), NFR-4 (no new observability) | No tasks — these are statements of non-impact. |
| Arch-review amendment 1: adapter line becomes no-op pass-through | Task 3 step 1 |
| Arch-review amendment 2: regression test mocks `apiClient.http.fetch`, not the hook | Task 1 (test mocks `getAuthenticatedApiClient` and inspects `mockHttp.fetch.mock.calls[0][0]`) |
| Arch-review risk: stale `descending` references | Task 3 step 3 + Task 4 step 4 (explicit greps) |
| Arch-review risk: regression test asserts wrong shape | Task 1 step 2 (RED step requires the test to fail before the fix) |
