### task: extract-page-window
**Files:** create `src/components/knowledge-base/pageWindow.ts`, create `src/components/knowledge-base/__tests__/pageWindow.test.ts`, modify `src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`.

- [ ] Step 1: Write `pageWindow.test.ts` first with an `it.each` table covering: totalPages 0 and 1, totalPages <= 5 (3 and 5), near-start (10,1) and (10,3), middle (10,4) and (10,7), near-end (10,8) and (10,10), plus boundary (6,3) and (6,4). Expected values derived by hand from the four branch rules in design.r1.md.
- [ ] Step 2: Run the test; confirm it fails (module missing).
- [ ] Step 3: Create `pageWindow.ts` exactly as in design.r1.md.
- [ ] Step 4: Run the test; confirm it passes.
- [ ] Step 5: In `KnowledgeBaseDocumentsTab.tsx`, import `getPageWindow` and replace the `Array.from({ length: Math.min(totalPages, 5) }, ...)` block with `getPageWindow(totalPages, pageNumber).map((pageNum) => { return (<button .../>); })`, leaving the button JSX unchanged.
- [ ] Step 6: `npm run lint` and `npx tsc --noEmit`; both clean.
- [ ] Step 7: Commit: `refactor(frontend): extract pagination page window helper`.

