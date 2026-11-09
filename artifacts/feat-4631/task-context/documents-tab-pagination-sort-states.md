### task: documents-tab-pagination-sort-states
**Files:** modify `src/components/knowledge-base/__tests__/KnowledgeBaseDocumentsTab.test.tsx`.

- [ ] Step 1: Pagination tests: with totalPages 3 render buttons 1-3; totalPages 10 at kbPage 1, 5, 10 shows [1-5], [3-7], [6-10]; prev disabled on page 1, next disabled on last page; clicking a page number updates the query args; footer range text.
- [ ] Step 2: Sort tests: click "Soubor" header (new column) -> sortBy `Filename`, descending false, page 1; click again -> descending true; click "Vytvořeno" (default column, currently desc) flips to asc.
- [ ] Step 3: State tests: loading skeleton (isLoading true), error message `Nepodařilo se načíst dokumenty.`, empty message, `canDelete` false hides trash button, delete confirm calls `mutateAsync('d1')` and closes dialog, cancel closes without call, row click with `firstChunkId` shows modal stub, row without it does not.
- [ ] Step 4: Run with coverage: `npm test -- KnowledgeBaseDocumentsTab pageWindow --watchAll=false --coverage --collectCoverageFrom='src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx'`; confirm >= 85% lines (>= 60% hard minimum). Add tests for any large uncovered branch.
- [ ] Step 5: `npm run lint` and `npm run build`; clean.
- [ ] Step 6: Commit: `test(frontend): cover KnowledgeBaseDocumentsTab pagination, sort and states`.
