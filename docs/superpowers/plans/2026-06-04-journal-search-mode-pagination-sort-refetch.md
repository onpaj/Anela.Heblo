# Fix Journal Search Pagination & Sorting (Auto-Refetch on Query-Key Change) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the silent data-staleness bug where changing page, page size, or sort while in journal search mode updates UI affordances but does not refetch data — by parameterizing the `enabled` flag of `useSearchJournalEntries` and binding it to `isSearchMode` at the only call site.

**Architecture:** Frontend-only, two-file change. Replace the hardcoded `enabled: false` on the search hook with an optional `enabled` parameter (default `false`, preserving "no request on mount" semantics for unaware callers). In `JournalList.tsx`, pass `isSearchMode` as the second argument so the query becomes live only when the user has applied a non-empty search filter — at which point React Query's standard query-key-change auto-refetch handles pagination, page-size, and sort updates. No new files, no schema/contract change, no backend touch.

**Tech Stack:** React 18 + TypeScript, `@tanstack/react-query` v5 (`useQuery` `enabled` semantics), Jest + React Testing Library.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `frontend/src/api/hooks/useJournal.ts` | Modify (lines 44–64) | Add optional `enabled?: boolean` (default `false`) parameter to `useSearchJournalEntries`; forward to `useQuery({ enabled })`. JSDoc the default. |
| `frontend/src/components/pages/Journal/JournalList.tsx` | Modify (lines 189–195) | Pass `isSearchMode` as the second argument to `useSearchJournalEntries`. No other change. |
| `frontend/src/api/hooks/__tests__/useJournal.test.ts` | Modify + extend | Update existing search test to honor the new default; add NFR-4 hook-level tests (enabled-false-no-mount-fetch, enabled-true-mount-fetch, enabled-true-key-change-auto-refetch). |
| `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` | Extend | Add component-level regression tests that simulate search-mode pagination, page-size change, and sort click; assert the search hook is re-invoked with the new params. |

No other files, no new modules.

---

## Task 1: Add failing hook-level test for `enabled: false` default (no fetch on mount)

This test pins down the backwards-compatibility guarantee from FR-1: when `enabled` is omitted, the hook must not fire on mount. It will fail today because the existing implementation already has `enabled: false` hardcoded — but after Task 3 strips the hardcoded value in favor of a parameter default, this test guards the equivalent behavior.

**Files:**
- Test: `frontend/src/api/hooks/__tests__/useJournal.test.ts`

- [ ] **Step 1: Open the file and append a new test inside the existing `describe("useSearchJournalEntries", ...)` block**

Insert this test immediately after the existing `it("should search journal entries successfully", ...)` test (which ends around line 217). Use the same `mockGetAuthenticatedApiClient` pattern.

```ts
    it("should not fetch on mount when enabled is omitted (default false)", async () => {
      const searchMock = jest.fn().mockResolvedValue({
        success: true,
        entries: [],
        totalCount: 0,
      });

      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_SearchJournalEntries: searchMock,
        baseUrl: "http://localhost:5001",
      } as any);

      renderHook(
        () =>
          useSearchJournalEntries({
            searchText: "test",
            pageNumber: 1,
            pageSize: 20,
          }),
        { wrapper: createWrapper },
      );

      // Wait long enough for any queued microtasks; the query must remain disabled.
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(searchMock).not.toHaveBeenCalled();
    });
```

- [ ] **Step 2: Run the new test in isolation to confirm it passes today**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useJournal.test.ts -t "should not fetch on mount when enabled is omitted" --no-coverage
```

Expected: PASS (because today the hook is hardcoded `enabled: false`). This is the **guard** test — it must continue to pass after Task 3 changes the implementation.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useJournal.test.ts
git commit -m "test: pin no-fetch-on-mount default for useSearchJournalEntries"
```

---

## Task 2: Add failing hook-level tests for `enabled: true` (fetch on mount; auto-refetch on params change)

These tests assert the new behavior from FR-1 (enabled=true fires on mount) and FR-3/FR-4/FR-5 (enabled=true auto-refetches when `params` change). They will **fail** today because the implementation hardcodes `enabled: false` and ignores any second argument — that's the RED step in TDD.

**Files:**
- Test: `frontend/src/api/hooks/__tests__/useJournal.test.ts`

- [ ] **Step 1: Append two more tests inside `describe("useSearchJournalEntries", ...)` after the test from Task 1**

```ts
    it("should fetch on mount when enabled is true", async () => {
      const mockSearchResponse = {
        ...mockJournalEntriesResponse,
        entries: [mockJournalEntriesResponse.entries[0]],
        totalCount: 1,
      };

      const searchMock = jest.fn().mockResolvedValue(mockSearchResponse);
      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_SearchJournalEntries: searchMock,
        baseUrl: "http://localhost:5001",
      } as any);

      const { result } = renderHook(
        () =>
          useSearchJournalEntries(
            {
              searchText: "test",
              pageNumber: 1,
              pageSize: 20,
            },
            true,
          ),
        { wrapper: createWrapper },
      );

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(searchMock).toHaveBeenCalledTimes(1);
      expect(result.current.data).toEqual(mockSearchResponse);
    });

    it("should auto-refetch when params change while enabled is true", async () => {
      const searchMock = jest.fn().mockResolvedValue({
        success: true,
        entries: [],
        totalCount: 0,
      });

      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_SearchJournalEntries: searchMock,
        baseUrl: "http://localhost:5001",
      } as any);

      const { result, rerender } = renderHook(
        ({ pageNumber }: { pageNumber: number }) =>
          useSearchJournalEntries(
            {
              searchText: "test",
              pageNumber,
              pageSize: 20,
            },
            true,
          ),
        {
          wrapper: createWrapper,
          initialProps: { pageNumber: 1 },
        },
      );

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });
      expect(searchMock).toHaveBeenCalledTimes(1);

      // Change the page number → query key changes → React Query refetches.
      rerender({ pageNumber: 2 });

      await waitFor(() => {
        expect(searchMock).toHaveBeenCalledTimes(2);
      });

      // The most recent call should reflect the new page number (positional arg #7).
      const lastCallArgs = searchMock.mock.calls[searchMock.mock.calls.length - 1];
      expect(lastCallArgs[6]).toBe(2); // pageNumber is the 7th positional arg
    });
```

- [ ] **Step 2: Run the new tests and confirm they FAIL (RED)**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useJournal.test.ts -t "useSearchJournalEntries" --no-coverage
```

Expected: The two new tests (`should fetch on mount when enabled is true`, `should auto-refetch when params change while enabled is true`) FAIL because the hook currently has `enabled: false` hardcoded and ignores any second argument — so the mock is never called and `result.current.isSuccess` never becomes `true`.

The first new test (`should not fetch on mount when enabled is omitted`) and the existing `should search journal entries successfully` test should still PASS.

- [ ] **Step 3: Commit the failing tests**

```bash
git add frontend/src/api/hooks/__tests__/useJournal.test.ts
git commit -m "test: add failing tests for enabled flag on useSearchJournalEntries"
```

---

## Task 3: Add `enabled` parameter to `useSearchJournalEntries` (turn RED to GREEN)

Implements FR-1: the hook accepts an optional `enabled` (default `false`) and forwards it to React Query. The default preserves today's "no request on mount" semantics for any unaware caller (NFR-3).

**Files:**
- Modify: `frontend/src/api/hooks/useJournal.ts:44-64`

- [ ] **Step 1: Edit `useJournal.ts`**

Replace the existing `useSearchJournalEntries` definition with the new signature. Use the Edit tool.

Replace this block (current lines 44–64):

```ts
export const useSearchJournalEntries = (params: SearchJournalParams) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, "search", params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_SearchJournalEntries(
        params.searchText || undefined,
        params.dateFrom || undefined,
        params.dateTo || undefined,
        params.productCodePrefix || undefined,
        params.tagIds || undefined,
        params.createdByUserId || undefined,
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDirection,
      );
    },
    enabled: false, // Only run when explicitly called
  });
};
```

with:

```ts
/**
 * Searches journal entries. The query is gated by `enabled` so that the page
 * controlling search mode can opt in only when the user has applied a non-empty
 * filter. Default is `false` to preserve the "no request on mount" behavior for
 * any future caller that does not explicitly enable the query.
 *
 * When `enabled` is `true`, React Query auto-refetches on every change to
 * `params` (pagination, page size, sort), so callers should rely on key-change
 * refetch rather than calling `.refetch()` manually after changing state.
 */
export const useSearchJournalEntries = (
  params: SearchJournalParams,
  enabled: boolean = false,
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, "search", params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_SearchJournalEntries(
        params.searchText || undefined,
        params.dateFrom || undefined,
        params.dateTo || undefined,
        params.productCodePrefix || undefined,
        params.tagIds || undefined,
        params.createdByUserId || undefined,
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDirection,
      );
    },
    enabled,
  });
};
```

- [ ] **Step 2: Run the failing hook tests; they should now PASS (GREEN)**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useJournal.test.ts -t "useSearchJournalEntries" --no-coverage
```

Expected: All four `useSearchJournalEntries` tests PASS, including:
- `should search journal entries successfully` (still calls `result.current.refetch()` explicitly — works with `enabled: false` default)
- `should not fetch on mount when enabled is omitted (default false)` (preserved guard)
- `should fetch on mount when enabled is true` (new — now passes)
- `should auto-refetch when params change while enabled is true` (new — now passes)

If the existing `should search journal entries successfully` test fails, do **not** weaken it — the explicit `refetch()` call is still expected to work. Investigate the failure and adjust the implementation, not the test.

- [ ] **Step 3: Run the full hooks test file to confirm no regressions**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useJournal.test.ts --no-coverage
```

Expected: All tests in the file pass.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useJournal.ts
git commit -m "feat: parameterize enabled flag on useSearchJournalEntries"
```

---

## Task 4: Add failing component-level test for pagination refetch in search mode

This is the **regression test** for the actual user-visible bug (FR-3). It will fail today because the call site at `JournalList.tsx:189` does not pass `enabled`, so changing `pageNumber` state does not trigger a refetch in search mode.

**Files:**
- Test: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx`

- [ ] **Step 1: Append a new test inside `describe("JournalList", ...)` after the last test (around line 484)**

The test enters search mode, then clicks a different page, and asserts that the search hook is **re-invoked with the new `pageNumber`** as part of its params argument (matching how the existing "Datum" header test on line 467 asserts hook-call shapes).

```tsx
  it("re-invokes the search hook with the new pageNumber when paginating in search mode", async () => {
    // Search returns 25 entries across 2 pages so page-2 button is visible.
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 25,
        currentPage: 1,
        pageSize: 20,
        totalPages: 2,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Enter search mode.
    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(screen.getByText("Filtrovat"));

    // Wait until search mode is active (page-2 button is rendered in the pagination footer).
    const page2Button = await screen.findByRole("button", { name: "2" });

    // Click page 2.
    fireEvent.click(page2Button);

    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      const params = lastCall?.[0];
      const enabled = lastCall?.[1];

      expect(enabled).toBe(true);
      expect(params).toMatchObject({
        searchText: "skincare",
        pageNumber: 2,
      });
    });
  });
```

- [ ] **Step 2: Run the new test and confirm it FAILS (RED)**

Run:
```bash
cd frontend && npx jest src/components/pages/Journal/__tests__/JournalList.test.tsx -t "re-invokes the search hook with the new pageNumber" --no-coverage
```

Expected: FAIL. The most likely failure mode is `expect(enabled).toBe(true)` fails with `received: undefined`, because the current call site passes only one argument to `useSearchJournalEntries`.

- [ ] **Step 3: Commit the failing test**

```bash
git add frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
git commit -m "test: add failing test for search-mode pagination refetch"
```

---

## Task 5: Bind `isSearchMode` to the search hook at the `JournalList` call site (turn RED to GREEN)

Implements FR-2 (and unlocks FR-3/FR-4/FR-5). The only change is adding `isSearchMode` as the second argument to the existing hook call. No other lines in `JournalList.tsx` are touched.

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx:189-195`

- [ ] **Step 1: Edit `JournalList.tsx`**

Replace this block (current lines 189–195):

```tsx
  const searchQuery = useSearchJournalEntries({
    searchText: searchTextFilter,
    pageNumber: pageNumber,
    pageSize: pageSize,
    sortBy: sortBy,
    sortDirection: sortDescending ? "DESC" : "ASC",
  });
```

with:

```tsx
  const searchQuery = useSearchJournalEntries(
    {
      searchText: searchTextFilter,
      pageNumber: pageNumber,
      pageSize: pageSize,
      sortBy: sortBy,
      sortDirection: sortDescending ? "DESC" : "ASC",
    },
    isSearchMode,
  );
```

Do not change `handlePageChange`, `handlePageSizeChange`, `handleSort`, `handleApplyFilters`, or `handleCloseModal`. Per FR-6, the explicit `searchQuery.refetch()` inside `handleApplyFilters` stays (so re-clicking **Filtrovat** with unchanged text still does something visible). Per FR-7, `handleCloseModal` stays. Per Decision 4 in the arch review, pagination/sort handlers remain pure state setters.

- [ ] **Step 2: Run the failing pagination test; it should now PASS (GREEN)**

Run:
```bash
cd frontend && npx jest src/components/pages/Journal/__tests__/JournalList.test.tsx -t "re-invokes the search hook with the new pageNumber" --no-coverage
```

Expected: PASS.

- [ ] **Step 3: Run the full `JournalList.test.tsx` to confirm no regressions**

Run:
```bash
cd frontend && npx jest src/components/pages/Journal/__tests__/JournalList.test.tsx --no-coverage
```

Expected: All tests pass, including the existing "should handle search input and apply search", "should handle Enter key press", "should clear search and return to normal mode", and "clicking the Datum header" tests. The existing `searchQuery.refetch()` call in `handleApplyFilters` is preserved by Task 5, so those tests continue to observe `mockSearchRefetch` being called.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/Journal/JournalList.tsx
git commit -m "fix: bind isSearchMode to useSearchJournalEntries in JournalList"
```

---

## Task 6: Add component-level regression tests for sort and page-size in search mode

Covers FR-4 (page-size) and FR-5 (sort) as component-level regression tests against the same bug class. Both follow the pattern established in Task 4: enter search mode, perform the interaction, assert the hook was re-invoked with the new params.

**Files:**
- Test: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx`

- [ ] **Step 1: Append two more tests after the test from Task 4**

```tsx
  it("re-invokes the search hook with the new sortBy when sorting in search mode", async () => {
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 1,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Enter search mode.
    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(screen.getByText("Filtrovat"));

    // Click "Datum" header to switch sortBy to "entryDate".
    const dateHeader = await screen.findByRole("columnheader", { name: /Datum/i });
    fireEvent.click(dateHeader);

    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      const params = lastCall?.[0];
      const enabled = lastCall?.[1];

      expect(enabled).toBe(true);
      expect(params).toMatchObject({
        searchText: "skincare",
        sortBy: "entryDate",
        sortDirection: "ASC",
      });
    });
  });

  it("re-invokes the search hook with the new pageSize and resets to page 1 in search mode", async () => {
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [mockJournalEntries[0]],
        totalCount: 100,
        currentPage: 1,
        pageSize: 20,
        totalPages: 5,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Enter search mode.
    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(screen.getByText("Filtrovat"));

    // Change page size to 50 via the <select> in the pagination footer.
    const pageSizeSelect = await screen.findByRole("combobox");
    fireEvent.change(pageSizeSelect, { target: { value: "50" } });

    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      const params = lastCall?.[0];
      const enabled = lastCall?.[1];

      expect(enabled).toBe(true);
      expect(params).toMatchObject({
        searchText: "skincare",
        pageSize: 50,
        pageNumber: 1, // page-size change must reset to page 1
      });
    });
  });
```

- [ ] **Step 2: Run the two new tests**

Run:
```bash
cd frontend && npx jest src/components/pages/Journal/__tests__/JournalList.test.tsx -t "re-invokes the search hook" --no-coverage
```

Expected: All three "re-invokes the search hook" tests PASS (the one from Task 4 plus the two new ones), because Task 5 has already enabled key-change refetch in search mode.

- [ ] **Step 3: Run the full component test file**

Run:
```bash
cd frontend && npx jest src/components/pages/Journal/__tests__/JournalList.test.tsx --no-coverage
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
git commit -m "test: add search-mode regression tests for sort and page-size"
```

---

## Task 7: Update the existing search test comment to reflect the new default semantics

The existing test at `useJournal.test.ts:181` is still valid (it explicitly calls `result.current.refetch()`), but its inline comment "Search queries are disabled by default (enabled: false in useSearchJournalEntries)" now reads as if the value is hardcoded. Per the architecture review's "Specification Amendments" note, update the comment so future readers understand `enabled = false` is now a default, not a constant.

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useJournal.test.ts:206-207`

- [ ] **Step 1: Edit the comment**

Replace:

```ts
      // Search queries are disabled by default (enabled: false in useSearchJournalEntries)
      // We need to manually trigger the query
      result.current.refetch();
```

with:

```ts
      // useSearchJournalEntries defaults to enabled=false; the only way to fire
      // the query without opting in via the second arg is an explicit refetch().
      result.current.refetch();
```

- [ ] **Step 2: Run the hooks test file**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useJournal.test.ts --no-coverage
```

Expected: All tests pass (this is a comment-only change).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useJournal.test.ts
git commit -m "docs: update useSearchJournalEntries test comment for new default"
```

---

## Task 8: Final validation — lint, build, and full frontend test suite

Per the project's `CLAUDE.md` validation gate ("FE: `npm run build` + `npm run lint`; all tests touched by the change must pass"). NFR-1 (no measurable regression in default-mode rendering) is implicitly covered because the default-mode code path is untouched, and the full test suite includes the prior `clicking the "Datum" header` test against `useJournalEntries`.

- [ ] **Step 1: Run lint**

Run:
```bash
cd frontend && npm run lint
```

Expected: no errors. (Warnings unrelated to the two touched files are acceptable but should not increase relative to `main`.)

- [ ] **Step 2: Run the production build**

Run:
```bash
cd frontend && npm run build
```

Expected: build completes successfully. The OpenAPI client is regenerated on build per `CLAUDE.md`; this is expected and should be a no-op for this change (no backend contract touched).

- [ ] **Step 3: Run the full frontend test suite touching journal**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useJournal.test.ts src/components/pages/Journal --no-coverage
```

Expected: all tests pass.

- [ ] **Step 4: No commit needed if no files changed**

If lint or build produced any auto-formatted output, stage and commit it:

```bash
git status
# if there are changes:
git add -A
git commit -m "chore: apply lint/format fixes after journal search fix"
```

If `git status` is clean, skip the commit — the validation has passed without modifications.

---

## Self-Review

**Spec coverage** — each FR/NFR maps to a task:

| Requirement | Covered by |
|------------|------------|
| FR-1 (hook accepts `enabled` flag, default `false`) | Tasks 1, 2, 3 |
| FR-2 (`JournalList` binds `enabled` to `isSearchMode`) | Tasks 4, 5 |
| FR-3 (pagination refetches in search mode) | Tasks 4, 5 |
| FR-4 (page-size refetches + resets to page 1 in search mode) | Task 6 (second new test) |
| FR-5 (sort refetches in search mode) | Task 6 (first new test) |
| FR-6 (`handleApplyFilters` keeps explicit refetch) | Task 5 Step 1 explicitly leaves the handler untouched |
| FR-7 (`handleCloseModal` unchanged) | Task 5 Step 1 explicitly leaves the handler untouched |
| FR-8 (loading/error states cover new auto-refetches) | No code change needed; existing `currentQuery.isLoading` / `currentQuery.error` paths fire on every refetch by React Query default. Manually verified by reading lines 287–308 of `JournalList.tsx`. |
| NFR-1 (performance: one extra request per interaction, cache preserved) | No code path adds batching/debouncing; cache preserved because `queryKey` is unchanged (Task 3 keeps `[...QUERY_KEYS.journal, "search", params]`). |
| NFR-2 (security: no new endpoint or data exposure) | No backend touched. |
| NFR-3 (backwards compat: optional flag with `false` default) | Task 3 sets `enabled: boolean = false`. |
| NFR-4 (testability: hook + component tests) | Tasks 1, 2 (hook); Tasks 4, 6 (component). |

**Placeholder scan** — searched for "TBD", "TODO", "implement later", "appropriate error handling", "similar to Task N", "Write tests for the above". No occurrences. Every code-changing step contains full code blocks.

**Type consistency** — the hook signature `useSearchJournalEntries(params: SearchJournalParams, enabled: boolean = false)` is used identically in Task 3 (implementation), Task 2 (hook tests), and Task 5 (call site). The call sites in the component tests (Tasks 4 and 6) read the second arg as `lastCall?.[1]` and assert it equals `true`, matching the runtime value of `isSearchMode` after the user clicks **Filtrovat** with non-empty text. `searchText`, `pageNumber`, `pageSize`, `sortBy`, `sortDirection` field names match the `SearchJournalParams` interface defined at `useJournal.ts:16-27`.

No issues found; plan is consistent.
