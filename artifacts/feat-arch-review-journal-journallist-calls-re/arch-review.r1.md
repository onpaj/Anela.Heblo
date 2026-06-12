# Architecture Review: Remove Manual `.refetch()` Calls from JournalList

## Skip Design: true

No visual or layout changes. This is a pure client-side refactor of query orchestration; the rendered UI, modal flow, pagination footer, and filter controls are unchanged.

## Architectural Fit Assessment

The proposed change **strengthens** existing patterns rather than introducing new ones. The codebase already uses React Query's key-change refetch as the standard mechanism across `useJournal.ts`, with explicit JSDoc on `useSearchJournalEntries` (lines 44–53) prohibiting the very anti-pattern this refactor removes. Mutation hooks (`useCreateJournalEntry`, `useUpdateJournalEntry`, `useDeleteJournalEntry`) already invalidate the `entries`, `search`, and `byProduct` query namespaces in their `onSuccess` callbacks (`useJournal.ts:98–108, 126–139, 151–161`), so post-mutation freshness is already guaranteed without `handleCloseModal` doing anything.

Integration points:
- **`JournalList.tsx` only** — three handlers. No props, no hook signatures, no module boundaries cross.
- **Tests at `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx`** — three existing tests (`should handle search input and apply search`, `should handle Enter key press in search input`, `should clear search and return to normal mode`) currently assert `mockRefetch.toHaveBeenCalled()` and **must be rewritten** to assert key-driven hook re-invocation instead. This pattern is already established in the file: `re-invokes the search hook with the new pageNumber when paginating in search mode` (lines 485–528) checks the hook's `mock.calls`, not `.refetch()`.

Note: actual line numbers in the source are slightly off from those quoted in `spec.r1.md` (e.g. `handleApplyFilters` is at lines 208–219, not 214–216). The function targets are unambiguous; the line offsets are immaterial.

One spec inaccuracy: the file lives at `frontend/src/components/pages/Journal/JournalList.tsx`, not `frontend/src/components/journal/JournalList.tsx` as written in the spec.

## Proposed Architecture

### Component Overview

```
User action (filter / clear / modal close)
        │
        ▼
  setState(...) calls          ← only mechanism that triggers refetch
        │
        ▼
  React commit phase
        │
        ▼
  useJournalEntries / useSearchJournalEntries re-evaluate
        │  (queryKey changes because params object changes)
        ▼
  React Query auto-refetch with CORRECT params
        │
        ▼
  Cache update → table re-renders with correct rows
```

For modal-close after a mutation, the path is:

```
Mutation success in modal
        │
        ▼
  useXxxJournalEntry.onSuccess → queryClient.invalidateQueries
        │
        ▼
  Active queries refetch
        │
        ▼
  Modal closes (handleCloseModal does NOT call refetch)
```

### Key Design Decisions

#### Decision 1: Trust React Query's key-change refetch, do not orchestrate it manually

**Options considered:**
- A: Remove `.refetch()` calls; rely on key-change refetch (proposed).
- B: Keep `.refetch()` but defer it via `setTimeout(0)` or `useEffect` to fire after state commits.
- C: Migrate to a single unified query hook that internally decides search vs. browse.

**Chosen approach:** A.

**Rationale:** B preserves the redundancy (two mechanisms doing the same job) and reintroduces the JSDoc contract violation in a more subtle form. C is out of scope per the spec and would be a larger refactor with no current benefit. A is the documented contract and is already used by every other consumer of these hooks.

#### Decision 2: Drop `async` from handlers that no longer await anything

**Options considered:**
- A: Make `handleApplyFilters` and `handleClearFilters` synchronous (remove `async`).
- B: Keep them `async` for symmetry/future use.

**Chosen approach:** A.

**Rationale:** YAGNI. Once the `await` is gone, the `async` is dead syntax. `handleKeyDown` calls `handleApplyFilters()` without awaiting it — already correct for a sync handler. No call site needs the returned promise.

#### Decision 3: Test strategy mirrors the existing key-change pattern

**Options considered:**
- A: Update the three `.toHaveBeenCalled()` assertions to inspect hook `mock.calls` for the updated params (matches lines 485–528 of the existing test file).
- B: Add a fetch-mock-level assertion counting HTTP requests.

**Chosen approach:** A.

**Rationale:** The hooks are already mocked in `JournalList.test.tsx`; the real React Query client never runs there. Asserting on hook call arguments is consistent with the file's existing pattern, requires no test infrastructure change, and is what the spec's "exactly one network call" requirement actually reduces to under the existing mocking strategy. B would require a meaningful test rewrite (real `QueryClient`, MSW or fetch-mock) which is out of scope.

## Implementation Guidance

### Directory / Module Structure

No new files, no moves. All changes confined to two files:

- `frontend/src/components/pages/Journal/JournalList.tsx` — remove three `.refetch()` blocks, drop redundant `async` keywords.
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — update three existing tests; optionally add a focused regression test asserting that `useSearchJournalEntries` / `useJournalEntries` are re-invoked with the new params after filter apply, clear, and modal close.

### Interfaces and Contracts

No public interface changes. All affected items are file-local:

- `handleApplyFilters: () => void` (was `async () => Promise<void>`)
- `handleClearFilters: () => void` (was `async () => Promise<void>`)
- `handleCloseModal: () => void` (unchanged signature; body shrinks)

External contracts preserved:
- `<JournalEntryModal onClose={handleCloseModal} />` — `onClose` is typed as `() => void`; passing a sync function is unchanged.
- Keyboard handler `handleKeyDown` already calls `handleApplyFilters()` without awaiting; no change needed.

### Data Flow

**Filter apply (after refactor):**
1. User clicks "Filtrovat" or presses Enter.
2. `handleApplyFilters` runs three `setState` calls: `setSearchTextFilter(text)`, `setIsSearchMode(text.trim() !== "")`, `setPageNumber(1)`.
3. React batches and commits.
4. `useSearchJournalEntries` re-renders with new params → `queryKey` changes → React Query fires the search request **once** (gated by `enabled = isSearchMode`).
5. If the user cleared the input (`isSearchMode` becomes `false`), `useJournalEntries` is the active query and may already have cached data for the current page; if not, it fetches.

**Filter clear:**
1. `handleClearFilters` sets input, filter, search-mode, and page back to defaults.
2. `useSearchJournalEntries` disables (`enabled = false`); `useJournalEntries` becomes the current query and re-renders with `pageNumber: 1` → fires once if not cached.

**Modal close after create/update/delete:**
1. Modal's mutation hook hits `onSuccess` → invalidates `entries`, `search`, `byProduct` keys.
2. React Query refetches active queries automatically.
3. `handleCloseModal` only resets `isModalOpen` and `editingEntryId` — no `.refetch()`.

**Modal close on cancel (no mutation):**
1. No invalidation fired by any mutation.
2. `handleCloseModal` does not refetch.
3. Net network requests: zero (correct behavior; the data on screen is what was on screen).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing tests asserting `mockRefetch.toHaveBeenCalled()` fail after the change | High | Rewrite the three tests at `JournalList.test.tsx:236, 267, 294` to assert on the hook's `mock.calls` (params object) — pattern already used at lines 485–528. This is mandatory work, not optional. |
| Browse query (`useJournalEntries`) does not invalidate when the modal closes without a mutation, leaving stale data visible if external changes occurred | Low | Acceptable. There is no concurrent-edit story in the Journal module today, and the previous unconditional refetch was a workaround, not a feature. If staleness ever becomes a concern, the right fix is `refetchOnWindowFocus` or a focused invalidation in a future change — not restoring the anti-pattern. |
| `handleApplyFilters` called with the same `searchTextInput` value twice in a row produces the same `params` object identity but different React Query `queryKey` array — and React Query may not refetch | Low | The `params` object is reconstructed on every render and the `queryKey` is `[...QUERY_KEYS.journal, "search", params]`. React Query uses deep equality (`isEqual`) on keys, so an identical filter applied twice will **not** refetch. This is correct behavior (no duplicate request for identical state) and matches the spec's "exactly one network call" requirement. No mitigation required, but document this in the test assertions. |
| `setPageNumber(1)` is a no-op if the user is already on page 1, and `setIsSearchMode(true)` may be a no-op if already in search mode — only the search-text change drives the key change | Low | Even when only one of the three setStates produces a value change, React Query still re-evaluates the `queryKey` on the next render and compares deep-equality. If nothing meaningful changed, no refetch fires — which is correct. The spec's "exactly one network call" requirement is upheld. |
| Removing `async` from a handler that an existing or future caller awaits | Low | `handleKeyDown` and the `<button onClick={handleApplyFilters}>` JSX do not await. No other call sites exist. Verified by `grep`. |

## Specification Amendments

1. **File path correction.** The spec writes the path as `frontend/src/components/journal/JournalList.tsx`; the actual path is `frontend/src/components/pages/Journal/JournalList.tsx`. Update both `Background` and `Functional Requirements` sections.

2. **Line numbers are advisory.** The spec cites lines 214–216, 233–237, 282–287; actual ranges in the current file are 208–219, 229–237, 278–287. Replace fixed line citations with handler-name references (`handleApplyFilters`, `handleClearFilters`, `handleCloseModal`) to avoid drift.

3. **Test rewrite is required, not optional.** NFR-4 frames tests as "add or update." Three existing tests (`should handle search input and apply search`, `should handle Enter key press in search input`, `should clear search and return to normal mode`) directly assert `mockRefetch.toHaveBeenCalled()` and will fail after the refactor. Reclassify this as a mandatory edit and reuse the existing key-change assertion pattern from lines 485–528 of the test file.

4. **Drop `async` keywords as part of the change.** FR-4 mentions "any now-unused `async`/`await` keywords" as mechanical follow-ups. Make this explicit: `handleApplyFilters` and `handleClearFilters` become synchronous.

5. **No new fetch-mock or MSW infrastructure.** The "exactly one network call" acceptance criteria under FR-1/FR-2/FR-3 should be reinterpreted in the unit-test layer as "the relevant hook is invoked with the new params at most once per state transition." Real network counting belongs to E2E coverage (already out of scope per the spec).

## Prerequisites

None.

- No migrations, config, or infrastructure changes required.
- React Query is already configured project-wide.
- Mutation `onSuccess` invalidation is already in place in `useJournal.ts`.
- The `enabled` gate on `useSearchJournalEntries` is already in place.

Implementation can begin immediately.