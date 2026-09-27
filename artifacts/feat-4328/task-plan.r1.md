# Fix useDqtRunDetail Query Key Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `useDqtRunDetail`'s React Query cache key include `resultPage`/`resultPageSize` so that changing pages fetches fresh data instead of silently replaying a cached earlier page.

**Architecture:** Extend `dataQualityKeys.runDetail` to accept `resultPage`/`resultPageSize` alongside `runId` and include all three in the returned key tuple (matching the existing convention used by every other detail-style key factory in `frontend/src/api/hooks/`, e.g. `useCatalog.ts`'s `detail(productCode, monthsBack)`). Update the single call site inside `useDqtRunDetail` to pass its own `resultPage`/`resultPageSize` into that factory. No backend, DTO, or public hook signature changes.

**Tech Stack:** React, TypeScript, `@tanstack/react-query` (TanStack Query), Jest + `@testing-library/react` (`renderHook`).

---

## File Structure

- **Modify:** `frontend/src/api/hooks/useDataQuality.ts`
  - `dataQualityKeys.runDetail` (lines 27-28): gains `resultPage`/`resultPageSize` params, includes them in the key tuple.
  - `useDqtRunDetail`'s `useQuery({ queryKey: ... })` call (line 65): passes `resultPage`/`resultPageSize` through to the updated factory.
- **Modify (test):** `frontend/src/api/hooks/__tests__/useDataQuality.test.ts`
  - Extend the existing `describe('useDqtRunDetail', ...)` block with a new test that re-renders the hook with a different `resultPage` and asserts a second API call fires with the new page (proving the cache key changed), and a companion test that re-rendering with unchanged args does **not** trigger a second call (proving caching still works for unchanged params).

No other files are created or touched. There is exactly one call site of `dataQualityKeys.runDetail` in the repo (confirmed by search), and the sole UI consumer, `frontend/src/components/data-quality/DqtRunDetail.tsx:133`, calls `useDqtRunDetail(runId)` with only one argument — its behavior is unaffected since `resultPage`/`resultPageSize` keep their existing defaults (`1`/`50`).

---

### task: fix-query-key-pagination

**Files:**
- Modify: `frontend/src/api/hooks/useDataQuality.ts:27-28` (query key factory)
- Modify: `frontend/src/api/hooks/useDataQuality.ts:65` (`useQuery` call site)
- Test: `frontend/src/api/hooks/__tests__/useDataQuality.test.ts` (extend `describe('useDqtRunDetail', ...)`)

- [ ] **Step 1: Write the failing test — page change triggers a new fetch**

Add this test inside the existing `describe('useDqtRunDetail', ...)` block in
`frontend/src/api/hooks/__tests__/useDataQuality.test.ts`, right after the
existing `'calls dataQuality_GetRunDetail with runId and paging'` test:

```typescript
it('fetches again when resultPage changes for the same runId', async () => {
    mockClient.dataQuality_GetRunDetail.mockResolvedValue({
        success: true,
        run: null,
        results: [],
        driftResults: [],
        totalDriftResults: 0,
    });

    const { wrapper } = createQueryClientWrapper();
    const { result, rerender } = renderHook(
        ({ page }: { page: number }) => useDqtRunDetail('run-1', page, 25),
        { wrapper, initialProps: { page: 1 } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockClient.dataQuality_GetRunDetail).toHaveBeenCalledTimes(1);
    expect(mockClient.dataQuality_GetRunDetail).toHaveBeenNthCalledWith(1, 'run-1', 1, 25);

    rerender({ page: 2 });

    await waitFor(() => expect(mockClient.dataQuality_GetRunDetail).toHaveBeenCalledTimes(2));
    expect(mockClient.dataQuality_GetRunDetail).toHaveBeenNthCalledWith(2, 'run-1', 2, 25);
});

it('does not refetch when re-rendered with the same runId and paging', async () => {
    mockClient.dataQuality_GetRunDetail.mockResolvedValue({
        success: true,
        run: null,
        results: [],
        driftResults: [],
        totalDriftResults: 0,
    });

    const { wrapper } = createQueryClientWrapper();
    const { result, rerender } = renderHook(
        ({ page }: { page: number }) => useDqtRunDetail('run-1', page, 25),
        { wrapper, initialProps: { page: 1 } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockClient.dataQuality_GetRunDetail).toHaveBeenCalledTimes(1);

    rerender({ page: 1 });

    // Give React Query a tick to (not) issue a refetch; staleTime keeps this cached.
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(mockClient.dataQuality_GetRunDetail).toHaveBeenCalledTimes(1);
});
```

- [ ] **Step 2: Run the tests to verify the first one fails**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useDataQuality.test.ts -t "resultPage changes" --verbose`

Expected: FAIL. The new test `'fetches again when resultPage changes for the same runId'`
fails because `dataQuality_GetRunDetail` is only called once total — the second
render with `page: 2` hits the React Query cache (the key is unchanged today)
and never calls the mocked API client a second time, so
`toHaveBeenCalledTimes(2)` never becomes true and the `waitFor` times out.

The second new test (`'does not refetch when re-rendered with the same runId and paging'`)
is expected to PASS even before the fix — it documents the caching behavior
that must be preserved, not the bug.

- [ ] **Step 3: Update the query key factory**

In `frontend/src/api/hooks/useDataQuality.ts`, replace:

```typescript
  runDetail: (runId: string) =>
    [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail'] as const,
```

with:

```typescript
  runDetail: (runId: string, resultPage: number, resultPageSize: number) =>
    [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail', resultPage, resultPageSize] as const,
```

- [ ] **Step 4: Update the `useQuery` call site to match**

In the same file, inside `useDqtRunDetail`, replace:

```typescript
  return useQuery({
    queryKey: dataQualityKeys.runDetail(runId ?? ''),
```

with:

```typescript
  return useQuery({
    queryKey: dataQualityKeys.runDetail(runId ?? '', resultPage, resultPageSize),
```

Leave every other line of `useDqtRunDetail` (the `queryFn`, `enabled`,
`staleTime`, `gcTime`) unchanged.

- [ ] **Step 5: Run the full test file to verify all tests pass**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useDataQuality.test.ts --verbose`

Expected: PASS — all tests in the file, including both new tests and the
pre-existing `useDqtRuns`/`useDqtRunDetail`/`useRunDqt` tests, are green.

- [ ] **Step 6: Run the DqtRunDetail component test to confirm no regression**

Run: `cd frontend && npx jest src/components/data-quality/__tests__/DqtRunDetail.test.tsx --verbose`

Expected: PASS — this file mocks `useDqtRunDetail` directly (see
`frontend/src/components/data-quality/__tests__/DqtRunDetail.test.tsx:7`), so
it is unaffected by the internal key-factory change, but this confirms the
component import/usage still compiles and behaves correctly.

- [ ] **Step 7: Commit**

```bash
cd frontend
git add src/api/hooks/useDataQuality.ts src/api/hooks/__tests__/useDataQuality.test.ts
git commit -m "fix(data-quality): include pagination params in useDqtRunDetail query key

The dataQualityKeys.runDetail cache key only included runId, so changing
resultPage/resultPageSize did not bust the React Query cache and silently
served the previously cached page. Include resultPage/resultPageSize in the
key, matching the convention already used by every other detail-style query
key in this codebase (e.g. useCatalog.ts's detail(productCode, monthsBack)).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E5xD3jgNhKaH4yRo8kEfpy"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (query key includes pagination params) → Step 3.
- FR-2 (`useDqtRunDetail` passes params into its own key) → Step 4.
- NFR-1 (backward compatibility — no signature change, no changes needed in `DqtRunDetail.tsx`) → confirmed in File Structure section; Step 6 verifies no regression.
- NFR-2 (test coverage proving the fix) → Step 1 (both new tests), Step 2 (verify the regression test fails first), Step 5 (verify it passes after the fix).
- Architecture Decision 1 (trailing scalar args, matching `useCatalog.ts`'s `detail(productCode, monthsBack)` shape) → Step 3's exact signature.
- Architecture risk mitigation ("test must assert on resulting behavior, not just that params reach the API client") → the new tests assert on `mockClient.dataQuality_GetRunDetail`'s call count and per-call arguments across a rerender, not merely that it was called once with the right params.

No gaps found.

**2. Placeholder scan:** No "TBD"/"TODO"/"add appropriate handling" placeholders. All code blocks are complete and copy-pasteable. No references to undefined types or functions.

**3. Type consistency:** `dataQualityKeys.runDetail(runId, resultPage, resultPageSize)` signature in Step 3 matches its call in Step 4 exactly (three positional args, same names and order) and matches the spec's and arch-review's proposed shape verbatim.
