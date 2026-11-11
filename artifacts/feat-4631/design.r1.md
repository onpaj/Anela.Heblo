# Design: Test coverage for KnowledgeBaseDocumentsTab

## Component Design

### pageWindow.ts
```ts
export function getPageWindow(totalPages: number, currentPage: number): number[] {
  const count = Math.min(Math.max(totalPages, 0), 5);
  return Array.from({ length: count }, (_, i) => {
    if (totalPages <= 5) return i + 1;
    if (currentPage <= 3) return i + 1;
    if (currentPage >= totalPages - 2) return totalPages - 4 + i;
    return currentPage - 2 + i;
  });
}
```
Component usage: `{getPageWindow(totalPages, pageNumber).map((pageNum) => (<button key={pageNum} ...>))}` with the button markup untouched.

### Test harness (KnowledgeBaseDocumentsTab.test.tsx)
- `jest.mock('../../../api/hooks/useKnowledgeBase', ...)` exposing `useKnowledgeBaseDocumentsQuery`, `useKnowledgeBaseContentTypesQuery`, `useDeleteKnowledgeBaseDocumentMutation` as jest.fn.
- `jest.mock('../ChunkDetailModal', ...)` renders `<div data-testid="chunk-modal">{chunkId}</div>`.
- `renderTab({ url = '/', canDelete = false })` wraps in `MemoryRouter initialEntries=[url]` with `<LocationProbe/>` (renders `useLocation().search` in `data-testid="search"`) and `<NavButtons/>` (buttons that call `navigate('/?kbPage=3...')` / `navigate(-1)`).
- `lastQueryArgs()` returns `mockQuery.mock.calls.at(-1)[0]`.
- Default mock return: `{ data: { documents: [...], totalPages: 10, totalCount: 200 }, isLoading: false, error: null }`.

### Test cases
1. Initial state: defaults; all params; invalid pageSize/page/sortDesc.
2. URL->state: one test per param via navigate; filename input value updates; back navigation.
3. State->URL: select status/contentType, apply filename filter (button and Enter), page size change, page click, clear filters removes params; defaults leave search empty.
4. Pagination: render buttons for totalPages 3, 10 with page 1, 5, 10 (via URL); prev/next disabled at ends; footer count text.
5. Sort: click active header flips desc; click other column -> asc, page reset.
6. Loading, error, empty states; delete confirm/cancel; row click with/without firstChunkId.

### pageWindow.test.ts
`it.each` table: (0,1)->[]; (1,1)->[1]; (3,2)->[1,2,3]; (5,5)->[1..5]; (10,1),(10,3)->[1..5]; (10,4)->[2..6]; (10,7)->[5..9]; (10,8),(10,10)->[6..10]; (6,3)->[1..5]; (6,4)->[2..6]... verify against the function.

## Data Schemas
Fixture `DocumentSummary`: `{ id: 'd1', filename: 'a.pdf', status: 'indexed', contentType: 'application/pdf', createdAt: new Date('2026-01-02'), indexedAt: new Date('2026-01-03'), firstChunkId: 'c1' }`; second doc with `firstChunkId` undefined and `indexedAt` undefined.
Query args shape: `{ pageNumber, pageSize, sortBy, sortDescending, filenameFilter?, statusFilter?, contentTypeFilter? }`.
