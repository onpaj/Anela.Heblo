### task: documents-tab-harness-and-url-sync
**Files:** create `src/components/knowledge-base/__tests__/KnowledgeBaseDocumentsTab.test.tsx`.

- [ ] Step 1: Build the harness per design.r1.md: hook mocks, `ChunkDetailModal` stub, `renderTab`, `LocationProbe`, navigation helper buttons, `lastQueryArgs`, fixtures. Check how other tests in the repo handle `lucide-react` (mock only if rendering fails).
- [ ] Step 2: Write "initial state" tests (defaults, all seven params, invalid pageSize, kbPage=0/negative, kbSortDesc=false).
- [ ] Step 3: Write "URL -> state" tests: one per param (kbPage, kbPageSize, kbSortBy, kbSortDesc, kbFilename, kbStatus, kbContentType) navigating after mount and using `waitFor` to assert `lastQueryArgs()`; filename input value test; `navigate(-1)` back test.
- [ ] Step 4: Write "state -> URL" tests: status select, content-type select, filename apply via button and Enter, page size change, page click, clear filters, defaults leave `search` empty.
- [ ] Step 5: Run `npm test -- KnowledgeBaseDocumentsTab --watchAll=false`; all pass. Do not assert call counts (effects may re-run).
- [ ] Step 6: Commit: `test(frontend): cover KnowledgeBaseDocumentsTab URL sync`.

