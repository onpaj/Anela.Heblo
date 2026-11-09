# Specification: Test coverage for KnowledgeBaseDocumentsTab

## Summary
`KnowledgeBaseDocumentsTab.tsx` has 0% line coverage (0/133). Add Jest + React Testing Library tests covering URL<->state synchronisation, the pagination sliding window and the sort toggle, and extract the page-window calculation into a pure function that is unit-tested for its four boundary cases.

## Background
The component keeps page, pageSize, sortBy, sortDesc and three filters (filename, status, contentType) in React state mirrored to URL params (`kbPage`, `kbPageSize`, `kbSortBy`, `kbSortDesc`, `kbFilename`, `kbStatus`, `kbContentType`). A URL->state effect supports back/forward navigation. A param rename or logic regression would silently break navigation. The pagination window (inline in JSX) has four branches: totalPages <= 5, pageNumber <= 3, pageNumber >= totalPages-2, middle. Sort toggle: same column flips direction; a different column sets that column ascending; both reset to page 1. Existing tests live in `frontend/src/components/knowledge-base/__tests__/` (Jest, mocks of `../../../api/hooks/useKnowledgeBase` and `lucide-react`).

## Functional Requirements

### FR-1: URL -> initial state
Component initialises state from URL params.
**Acceptance criteria:**
- With no params, the query hook is called with pageNumber 1, pageSize 20, sortBy `CreatedAt`, sortDescending true, and undefined filters.
- With all seven params set, the hook receives matching values.
- Invalid `kbPageSize` (e.g. 15) falls back to 20; `kbPage=0` or negative clamps to 1; `kbSortDesc=false` yields sortDescending false.

### FR-2: URL -> state on navigation (back/forward)
**Acceptance criteria:**
- After render, changing the URL (via a router navigate from a helper component, or history back) updates the hook arguments for each of page, pageSize, sortBy, sortDesc, filename, status, contentType.
- The filename input value reflects `kbFilename` after navigation.
- Each param is covered by at least one test so a rename fails a test.

### FR-3: State -> URL sync
**Acceptance criteria:**
- Non-default state writes the param; default state removes it (replace navigation, no history growth).
- Applying filters, changing status/content-type selects, changing page size, clicking pages updates the location search accordingly.

### FR-4: Pagination window
Extract `getPageWindow(totalPages, currentPage)` returning the array of page numbers shown (max 5).
**Acceptance criteria:**
- totalPages <= 5 returns 1..totalPages (including 1 and 0 -> empty).
- currentPage <= 3 (totalPages > 5) returns [1..5].
- currentPage >= totalPages-2 returns the last five pages.
- Middle returns currentPage-2..currentPage+2.
- Component rendering uses the function; behaviour unchanged. A component test verifies rendered page buttons for at least one case and prev/next disabled states at boundaries.

### FR-5: Sort toggle
**Acceptance criteria:**
- Clicking the active column header flips sortDescending; clicking another column sets sortBy to it with sortDescending false; page resets to 1.
- Verified via query hook arguments and URL.

### FR-6: Supporting states (to reach reasonable coverage)
- Loading skeleton, error message, empty-state message, clear-filters resetting everything, delete flow (canDelete shows trash button, confirm calls mutateAsync with id, cancel closes), row click opens ChunkDetailModal only when `firstChunkId` is set.

## Non-Functional Requirements

### NFR-1: Performance
Tests run fast (no real timers/network); all hooks mocked.

### NFR-2: Security
None; test-only plus a behaviour-preserving refactor.

### NFR-3: Conventions
Tests in `frontend/src/components/knowledge-base/__tests__/`, `npm run build` and `npm run lint` pass; line coverage of the component >= 60% (target >= 85%).

## Data Model
No changes. Mocked `DocumentSummary` objects: id, filename, status, contentType, createdAt/indexedAt as Date, firstChunkId.

## API / Interface Design
New export `getPageWindow(totalPages: number, currentPage: number): number[]` in `frontend/src/components/knowledge-base/pageWindow.ts`. No API or UI change.

## Dependencies
Jest, @testing-library/react, react-router-dom MemoryRouter, existing hook module `api/hooks/useKnowledgeBase`.

## Out of Scope
Changing component behaviour or the double-effect design; E2E tests; tests for other knowledge-base components; fixing any quirks found (report them instead).

## Open Questions
None.

## Status: COMPLETE
