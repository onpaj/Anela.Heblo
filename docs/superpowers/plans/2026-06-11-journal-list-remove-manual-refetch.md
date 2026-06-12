# Remove Manual `.refetch()` Calls from JournalList Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove three manual `.refetch()` invocations from `JournalList.tsx` so that React Query's automatic key-change refetch is the sole refetch mechanism, eliminating duplicate network calls and stale-data flashes.

**Architecture:** Pure client-side refactor of query orchestration. Strip three `.refetch()` blocks from the three handlers (`handleApplyFilters`, `handleClearFilters`, `handleCloseModal`), drop the now-dead `async` keywords, and rewrite three existing tests that asserted on `mockRefetch.toHaveBeenCalled()` to instead assert on hook re-invocation with the new params object (matching the pattern already established at `JournalList.test.tsx:485–528`). No hook signatures, no public interfaces, no API contracts change.

**Tech Stack:** React 18, TypeScript, `@tanstack/react-query` v5 (already configured), Jest 29 + React Testing Library, `JournalList.tsx` (page component), `useJournal.ts` (existing hook module — not modified).

---

## File Structure

Only two files are touched. No new files, no moves.

- **Modify** `frontend/src/components/pages/Journal/JournalList.tsx`
  - `handleApplyFilters` (lines 208–219): drop the body's `if/else refetch` block (lines 213–218); remove `async`.
  - `handleClearFilters` (lines 229–237): drop the trailing `await entriesQuery.refetch()` (lines 235–236); remove `async`.
  - `handleCloseModal` (lines 278–287): drop the trailing `if/else refetch` block (lines 281–286).
- **Modify** `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx`
  - Rewrite test `should handle search input and apply search` (lines 236–265).
  - Rewrite test `should handle Enter key press in search input` (lines 267–292).
  - Rewrite test `should clear search and return to normal mode` (lines 294–341).
  - Add one new test for `handleCloseModal` to lock in the regression coverage required by spec FR-3.

Files **not** touched (verify by `git diff --stat` after task 6):
- `frontend/src/api/hooks/useJournal.ts`
- `frontend/src/components/JournalEntryModal.tsx`
- any other Journal-module file

---

## Pre-flight Verification

### Task 0: Confirm the starting state

**Files:**
- Read: `frontend/src/components/pages/Journal/JournalList.tsx`
- Read: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx`

- [ ] **Step 1: Verify the three target call sites exist**

Run:
```bash
grep -n "refetch" frontend/src/components/pages/Journal/JournalList.tsx
```

Expected output (line numbers approximate; handler-name attribution is authoritative):
```
215:      await searchQuery.refetch();
217:      await entriesQuery.refetch();
236:    await entriesQuery.refetch();
283:      searchQuery.refetch();
285:      entriesQuery.refetch();
```

If the number of lines or the handler context differs, STOP and reconcile with the spec before continuing — drift means the source has changed since the plan was written.

- [ ] **Step 2: Verify the three target tests exist**

Run:
```bash
grep -n "mockSearchRefetch\|mockRefetch" frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
```

Expected: matches inside the three test bodies on lines roughly 236–341. If absent, STOP and reconcile.

- [ ] **Step 3: Verify the existing test suite is green before touching anything**

Run from `frontend/`:
```bash
npm test -- --testPathPattern=JournalList.test.tsx --watchAll=false
```

Expected: all tests pass. If any fail, STOP — fix the pre-existing failures or escalate before modifying the file.

---

## Test-First Refactor

### Task 1: Rewrite test `should handle search input and apply search`

**Goal:** Assert that clicking "Filtrovat" causes `useSearchJournalEntries` to be re-invoked with the new `searchText`, `pageNumber: 1`, and `enabled = true`. Remove the `mockSearchRefetch` assertion.

**Files:**
- Modify: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` (lines 236–265)

- [ ] **Step 1: Replace the test body**

Replace the entire test (lines 236–265) with:

```typescript
  it("re-invokes the search hook with the new searchText and enabled=true when applying filters", async () => {
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

    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");
    const filterButton = screen.getByText("Filtrovat");

    fireEvent.change(searchInput, { target: { value: "skincare" } });
    fireEvent.click(filterButton);

    let searchParams: any;
    let searchEnabled: any;
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      searchParams = lastCall?.[0];
      searchEnabled = lastCall?.[1];

      expect(searchEnabled).toBe(true);
    });

    expect(searchParams).toMatchObject({
      searchText: "skincare",
      pageNumber: 1,
    });
  });
```

- [ ] **Step 2: Run the rewritten test against the **unchanged** source**

Run from `frontend/`:
```bash
npm test -- --testPathPattern=JournalList.test.tsx -t "re-invokes the search hook with the new searchText" --watchAll=false
```

Expected: **PASS**. Rationale: the assertion targets hook re-invocation, which already happens because the `setState` calls fire regardless of whether `.refetch()` is also called. The test must be green *before* the source change so we know the source change didn't break it later.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
git commit -m "test: assert search hook re-invocation on filter apply instead of mockRefetch"
```

---

### Task 2: Rewrite test `should handle Enter key press in search input`

**Goal:** Assert the same hook-reinvocation contract for the Enter-key path.

**Files:**
- Modify: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` (lines 267–292)

- [ ] **Step 1: Replace the test body**

Replace the entire test with:

```typescript
  it("re-invokes the search hook with the new searchText when Enter is pressed in the search input", async () => {
    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        totalPages: 0,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    const searchInput = screen.getByPlaceholderText("Hledat v záznamech...");

    fireEvent.change(searchInput, { target: { value: "test search" } });
    fireEvent.keyDown(searchInput, { key: "Enter", code: "Enter" });

    let searchParams: any;
    let searchEnabled: any;
    await waitFor(() => {
      const calls = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls;
      const lastCall = calls[calls.length - 1];
      searchParams = lastCall?.[0];
      searchEnabled = lastCall?.[1];

      expect(searchEnabled).toBe(true);
    });

    expect(searchParams).toMatchObject({
      searchText: "test search",
      pageNumber: 1,
    });
  });
```

- [ ] **Step 2: Run the rewritten test against the **unchanged** source**

Run:
```bash
npm test -- --testPathPattern=JournalList.test.tsx -t "re-invokes the search hook with the new searchText when Enter" --watchAll=false
```

Expected: **PASS**.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
git commit -m "test: assert search hook re-invocation on Enter key press"
```

---

### Task 3: Rewrite test `should clear search and return to normal mode`

**Goal:** Assert that clicking "Vymazat" exits search mode by passing `enabled = false` to `useSearchJournalEntries` and re-invoking `useJournalEntries` with `pageNumber: 1`. No `mockRefetch` assertion.

**Files:**
- Modify: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` (lines 294–341)

- [ ] **Step 1: Replace the test body**

Replace the entire test with:

```typescript
  it("disables the search hook and re-invokes the entries hook when filters are cleared", async () => {
    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: mockJournalEntries,
        totalCount: 2,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: jest.fn().mockResolvedValue({}),
    } as any);

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
    fireEvent.change(searchInput, { target: { value: "test" } });
    fireEvent.keyDown(searchInput, { key: "Enter" });

    await waitFor(() => {
      const lastSearchCall = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls.at(-1);
      expect(lastSearchCall?.[1]).toBe(true);
    });

    // Clear filters.
    const clearButton = screen.getByText("Vymazat");
    fireEvent.click(clearButton);

    await waitFor(() => {
      const lastSearchCall = (mockUseJournalHooks.useSearchJournalEntries as jest.Mock).mock.calls.at(-1);
      expect(lastSearchCall?.[1]).toBe(false);
    });

    const lastEntriesCall = (mockUseJournalHooks.useJournalEntries as jest.Mock).mock.calls.at(-1);
    expect(lastEntriesCall?.[0]).toMatchObject({
      pageNumber: 1,
    });

    // The search input should also be cleared visually.
    expect(searchInput).toHaveValue("");
  });
```

- [ ] **Step 2: Run the rewritten test against the **unchanged** source**

Run:
```bash
npm test -- --testPathPattern=JournalList.test.tsx -t "disables the search hook and re-invokes the entries hook" --watchAll=false
```

Expected: **PASS**.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
git commit -m "test: assert entries hook re-invocation and search disabled on clear filters"
```

---

### Task 4: Add regression test for `handleCloseModal` not calling refetch

**Goal:** Lock in spec FR-3: after the source change, `handleCloseModal` must NOT cause an additional invocation of `useJournalEntries` or `useSearchJournalEntries` beyond what the modal-open/close React state transition naturally produces. Today (before the source change) the test FAILS because the source calls `.refetch()`. After Task 5 it PASSES because the source no longer does.

This is a "red-then-green" test by design — it is the verification step for FR-3.

**Files:**
- Modify: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` (add as last test inside `describe("JournalList", ...)`)

- [ ] **Step 1: Add the test before the closing `});` of the `describe` block**

Insert the following test immediately before the final `});` of the `describe("JournalList", ...)` block (around current line 615):

```typescript
  it("does not call refetch on entriesQuery or searchQuery when the modal closes", async () => {
    const entriesRefetch = jest.fn().mockResolvedValue({});
    const searchRefetch = jest.fn().mockResolvedValue({});

    mockUseJournalHooks.useJournalEntries.mockReturnValue({
      data: {
        entries: mockJournalEntries,
        totalCount: 2,
        currentPage: 1,
        pageSize: 20,
        totalPages: 1,
      },
      isLoading: false,
      error: null,
      refetch: entriesRefetch,
    } as any);

    mockUseJournalHooks.useSearchJournalEntries.mockReturnValue({
      data: {
        entries: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        totalPages: 0,
      },
      isLoading: false,
      error: null,
      refetch: searchRefetch,
    } as any);

    render(<JournalList />, { wrapper: createWrapper });

    // Open the "new entry" modal.
    fireEvent.click(screen.getByTestId("add-journal-entry"));

    // Close the modal: dispatch Escape on the document body — the modal listens for Escape
    // and calls its onClose prop, which is JournalList's handleCloseModal.
    // (We avoid clicking inside the modal because the modal's internals are not under test here.)
    fireEvent.keyDown(document.body, { key: "Escape", code: "Escape" });

    // Allow any pending state updates / effects to flush.
    await waitFor(() => {
      // Sanity: refetch on entries / search must remain at zero invocations from handleCloseModal.
      expect(entriesRefetch).not.toHaveBeenCalled();
      expect(searchRefetch).not.toHaveBeenCalled();
    });
  });
```

- [ ] **Step 2: Run the new test against the **unchanged** source — expect FAIL**

Run:
```bash
npm test -- --testPathPattern=JournalList.test.tsx -t "does not call refetch on entriesQuery or searchQuery when the modal closes" --watchAll=false
```

Expected: **FAIL** with one of:
- `expected jest.fn() not to be called, but was called 1 time` on `entriesRefetch`, OR
- the modal's Escape handler doesn't fire (in which case verify the modal is actually mounting — render output should contain it). If the modal does not respond to Escape, fall back to an alternate close trigger: query for the modal's close button by `data-testid` or `aria-label` and click it instead. Adjust the test to use whatever close affordance the modal exposes. Acceptance is: the modal closes AND `entriesRefetch` is called once with the unchanged source.

If after a reasonable substitution (clicking the modal's close button) the test still does not fail, STOP and inspect: it likely means the modal isn't being mounted by the test, in which case the test must first assert that the modal is visible before triggering close. Do not skip this red-state check — without it we have no proof the new test exercises the handler.

- [ ] **Step 3: Commit (red)**

```bash
git add frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
git commit -m "test: add failing regression for handleCloseModal not calling refetch"
```

---

## Source Refactor

### Task 5: Strip `.refetch()` from all three handlers and drop dead `async`

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx` (handlers `handleApplyFilters`, `handleClearFilters`, `handleCloseModal`)

- [ ] **Step 1: Replace `handleApplyFilters`**

Find this block (currently lines 208–219):

```typescript
  // Handler for applying filters
  const handleApplyFilters = async () => {
    setSearchTextFilter(searchTextInput);
    setIsSearchMode(searchTextInput.trim() !== "");
    setPageNumber(1); // Reset to first page when applying filters

    // Force data reload by refetching
    if (searchTextInput.trim()) {
      await searchQuery.refetch();
    } else {
      await entriesQuery.refetch();
    }
  };
```

Replace with:

```typescript
  // Handler for applying filters
  const handleApplyFilters = () => {
    setSearchTextFilter(searchTextInput);
    setIsSearchMode(searchTextInput.trim() !== "");
    setPageNumber(1); // Reset to first page when applying filters
  };
```

- [ ] **Step 2: Replace `handleClearFilters`**

Find this block (currently lines 229–237):

```typescript
  // Handler for clearing all filters
  const handleClearFilters = async () => {
    setSearchTextInput("");
    setSearchTextFilter("");
    setIsSearchMode(false);
    setPageNumber(1); // Reset to first page when clearing filters

    // Force data reload by refetching
    await entriesQuery.refetch();
  };
```

Replace with:

```typescript
  // Handler for clearing all filters
  const handleClearFilters = () => {
    setSearchTextInput("");
    setSearchTextFilter("");
    setIsSearchMode(false);
    setPageNumber(1); // Reset to first page when clearing filters
  };
```

- [ ] **Step 3: Replace `handleCloseModal`**

Find this block (currently lines 278–287):

```typescript
  const handleCloseModal = () => {
    setIsModalOpen(false);
    setEditingEntryId(null);
    // Refetch data after modal closes
    if (isSearchMode) {
      searchQuery.refetch();
    } else {
      entriesQuery.refetch();
    }
  };
```

Replace with:

```typescript
  const handleCloseModal = () => {
    setIsModalOpen(false);
    setEditingEntryId(null);
  };
```

- [ ] **Step 4: Verify `searchQuery` and `entriesQuery` are still used elsewhere**

Run:
```bash
grep -n "searchQuery\|entriesQuery" frontend/src/components/pages/Journal/JournalList.tsx
```

Expected: lines around 182 (`entriesQuery = useJournalEntries(...)`), 189 (`searchQuery = useSearchJournalEntries(...)`), and 200 (`currentQuery = isSearchMode ? searchQuery : entriesQuery`). All three are still consumed by `currentQuery`, so do NOT remove the variable bindings.

- [ ] **Step 5: Verify no `.refetch(` calls remain in the file**

Run:
```bash
grep -n "\.refetch(" frontend/src/components/pages/Journal/JournalList.tsx
```

Expected: **no output** (zero matches).

- [ ] **Step 6: Verify no stray `async` keywords remain on the touched handlers**

Run:
```bash
grep -nE "const handle(ApplyFilters|ClearFilters|CloseModal) = (async )?\(\)" frontend/src/components/pages/Journal/JournalList.tsx
```

Expected: three matches, none containing `async`.

- [ ] **Step 7: Run all four tests touched by tasks 1–4 — expect all PASS**

Run:
```bash
npm test -- --testPathPattern=JournalList.test.tsx --watchAll=false
```

Expected: **all tests in the suite pass**, including the four that were touched (re-invocation x3 + no-refetch regression x1) and the pre-existing tests around sort/pagination.

If a test fails, do NOT modify the test — investigate the source. The only legitimate failure modes after this refactor are:
- A test that was relying on the side effect of `.refetch()` (none should — the four we touched are the only ones with refetch assertions).
- A pre-existing flaky test (re-run once before reporting).

- [ ] **Step 8: Commit (green)**

```bash
git add frontend/src/components/pages/Journal/JournalList.tsx
git commit -m "refactor: remove manual .refetch() from JournalList handlers

Rely on React Query's key-change refetch instead. Eliminates duplicate
network requests and stale-data flashes from handleApplyFilters,
handleClearFilters, and handleCloseModal. handleApplyFilters and
handleClearFilters are now synchronous since they no longer await."
```

---

## Validation

### Task 6: Whole-file build & lint pass

**Files:** none modified.

- [ ] **Step 1: TypeScript build**

Run from `frontend/`:
```bash
npm run build
```

Expected: clean build, no TypeScript errors. The signature changes (`async () => Promise<void>` → `() => void`) on `handleApplyFilters` and `handleClearFilters` should not introduce any errors because:
- `handleKeyDown` calls `handleApplyFilters()` without awaiting it (line 224 — unchanged).
- The "Filtrovat" and "Vymazat" buttons consume the handler via `onClick={...}` (the `onClick` prop type is `(e: MouseEvent) => void`, which accepts a void-returning function).
- `handleCloseModal` was already non-async; modal `onClose: () => void` is unchanged.

If a TypeScript error appears around any of those call sites, STOP — something in the surrounding code has drifted.

- [ ] **Step 2: ESLint**

Run from `frontend/`:
```bash
npm run lint
```

Expected: no new lint errors or warnings. The refactor should reduce lint pressure (no more unused `await`, no async-without-await complaints) rather than add to it.

- [ ] **Step 3: Confirm the diff is surgical**

Run:
```bash
git diff --stat main...HEAD -- frontend/src/components/pages/Journal/
```

Expected: only two files in the diff:
- `frontend/src/components/pages/Journal/JournalList.tsx` — approximately 8 insertions / 16 deletions
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — approximately 100 insertions / 100 deletions (three rewrites + one addition)

If `useJournal.ts`, `JournalEntryModal.tsx`, or any other file appears in the diff, STOP and reconcile against spec FR-4 ("no changes to `useJournal.ts`, the mutation hooks, or any other Journal module file").

- [ ] **Step 4: Final full-suite test run**

Run:
```bash
npm test -- --testPathPattern=JournalList.test.tsx --watchAll=false
```

Expected: all tests pass.

- [ ] **Step 5: Commit (only if Step 2 added lint auto-fixes; otherwise skip)**

If `npm run lint` made any auto-fix edits:

```bash
git add -u
git commit -m "chore: apply lint auto-fixes after refetch removal"
```

Otherwise skip this step — no commit needed.

---

## Self-Review Mapping (spec → task)

| Spec section | Implemented by |
|--------------|----------------|
| FR-1 (remove refetch from `handleApplyFilters`) | Task 5, Step 1 |
| FR-1 acceptance (exactly one hook invocation, no stale flash) | Tasks 1 & 4 |
| FR-2 (remove refetch from `handleClearFilters`) | Task 5, Step 2 |
| FR-2 acceptance | Task 3 |
| FR-3 (remove refetch from `handleCloseModal`) | Task 5, Step 3 |
| FR-3 acceptance (mutation `onSuccess` → invalidation already in place; cancel-close = zero network) | Task 4 |
| FR-4 (no changes outside the three handlers; drop dead `async`) | Task 5 + Task 6, Step 3 |
| NFR-1 (one HTTP call per state transition) | Task 6, Step 4 (proven indirectly via hook-invocation assertions) |
| NFR-2 (no stale-data flash) | Achieved by the refactor itself — there is no longer a wrong-params request to land first |
| NFR-3 (honor JSDoc contract) | Task 5 |
| NFR-4 (existing tests pass, new/updated tests cover the new behavior) | Tasks 1–4, validated by Task 6 Step 4 |
| Arch amendment 1 (file-path correction) | Reflected throughout this plan (`frontend/src/components/pages/Journal/JournalList.tsx`) |
| Arch amendment 2 (line numbers advisory, handler names authoritative) | Each task names the handler explicitly |
| Arch amendment 3 (test rewrite is mandatory) | Tasks 1–3 are mandatory rewrites |
| Arch amendment 4 (drop `async` keywords) | Task 5, Steps 1–2, verified Step 6 |
| Arch amendment 5 (no fetch-mock infrastructure) | Tests assert on hook `mock.calls`, no MSW / fetch-mock added |
