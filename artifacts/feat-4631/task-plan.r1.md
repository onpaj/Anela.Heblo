# Implementation Plan: KnowledgeBaseDocumentsTab test coverage

Goal: raise `KnowledgeBaseDocumentsTab.tsx` coverage from 0% to >= 85% lines with RTL tests and a unit-tested `getPageWindow` helper.
All commands run from `frontend/`. Run tests with `npm test -- <pattern> --watchAll=false`.

### task: extract-page-window
**Files:** create `src/components/knowledge-base/pageWindow.ts`, create `src/components/knowledge-base/__tests__/pageWindow.test.ts`, modify `src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`.

- [ ] Step 1: Write `pageWindow.test.ts` first with an `it.each` table covering: totalPages 0 and 1, totalPages <= 5 (3 and 5), near-start (10,1) and (10,3), middle (10,4) and (10,7), near-end (10,8) and (10,10), plus boundary (6,3) and (6,4). Expected values derived by hand from the four branch rules in design.r1.md.
- [ ] Step 2: Run the test; confirm it fails (module missing).
- [ ] Step 3: Create `pageWindow.ts` exactly as in design.r1.md.
- [ ] Step 4: Run the test; confirm it passes.
- [ ] Step 5: In `KnowledgeBaseDocumentsTab.tsx`, import `getPageWindow` and replace the `Array.from({ length: Math.min(totalPages, 5) }, ...)` block with `getPageWindow(totalPages, pageNumber).map((pageNum) => { return (<button .../>); })`, leaving the button JSX unchanged.
- [ ] Step 6: `npm run lint` and `npx tsc --noEmit`; both clean.
- [ ] Step 7: Commit: `refactor(frontend): extract pagination page window helper`.

### task: documents-tab-harness-and-url-sync
**Files:** create `src/components/knowledge-base/__tests__/KnowledgeBaseDocumentsTab.test.tsx`.

- [ ] Step 1: Build the harness per design.r1.md: hook mocks, `ChunkDetailModal` stub, `renderTab`, `LocationProbe`, navigation helper buttons, `lastQueryArgs`, fixtures. Check how other tests in the repo handle `lucide-react` (mock only if rendering fails).
- [ ] Step 2: Write "initial state" tests (defaults, all seven params, invalid pageSize, kbPage=0/negative, kbSortDesc=false).
- [ ] Step 3: Write "URL -> state" tests: one per param (kbPage, kbPageSize, kbSortBy, kbSortDesc, kbFilename, kbStatus, kbContentType) navigating after mount and using `waitFor` to assert `lastQueryArgs()`; filename input value test; `navigate(-1)` back test.
- [ ] Step 4: Write "state -> URL" tests: status select, content-type select, filename apply via button and Enter, page size change, page click, clear filters, defaults leave `search` empty.
- [ ] Step 5: Run `npm test -- KnowledgeBaseDocumentsTab --watchAll=false`; all pass. Do not assert call counts (effects may re-run).
- [ ] Step 6: Commit: `test(frontend): cover KnowledgeBaseDocumentsTab URL sync`.

### task: documents-tab-pagination-sort-states
**Files:** modify `src/components/knowledge-base/__tests__/KnowledgeBaseDocumentsTab.test.tsx`.

- [ ] Step 1: Pagination tests: with totalPages 3 render buttons 1-3; totalPages 10 at kbPage 1, 5, 10 shows [1-5], [3-7], [6-10]; prev disabled on page 1, next disabled on last page; clicking a page number updates the query args; footer range text.
- [ ] Step 2: Sort tests: click "Soubor" header (new column) -> sortBy `Filename`, descending false, page 1; click again -> descending true; click "Vytvořeno" (default column, currently desc) flips to asc.
- [ ] Step 3: State tests: loading skeleton (isLoading true), error message `Nepodařilo se načíst dokumenty.`, empty message, `canDelete` false hides trash button, delete confirm calls `mutateAsync('d1')` and closes dialog, cancel closes without call, row click with `firstChunkId` shows modal stub, row without it does not.
- [ ] Step 4: Run with coverage: `npm test -- KnowledgeBaseDocumentsTab pageWindow --watchAll=false --coverage --collectCoverageFrom='src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx'`; confirm >= 85% lines (>= 60% hard minimum). Add tests for any large uncovered branch.
- [ ] Step 5: `npm run lint` and `npm run build`; clean.
- [ ] Step 6: Commit: `test(frontend): cover KnowledgeBaseDocumentsTab pagination, sort and states`.
