# KB Feedback Detail — Modal Redesign

**Date:** 2026-03-26
**Scope:** Frontend only — layout change, no backend changes

## Problem

The current KB Feedback Browser shows record details in a fixed-width right panel (`w-96`). This panel is narrow, compresses the main table, and provides limited space for long question/answer text.

## Goal

Replace the right panel with a centered modal dialog that gives each record's content more space to breathe, following the established modal pattern already used across the app.

## Components Changed

### `FeedbackDetailPanel.tsx` → `FeedbackDetailModal.tsx`

Rename the file. The component is converted from a side-panel to a modal overlay:

**Wrapper (overlay):**
```
fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50
```

**Inner box:**
```
bg-white rounded-lg shadow-xl max-w-3xl w-full mx-4 max-h-[90vh] overflow-hidden flex flex-col
```

**Header:** full-width with `p-6 border-b`, same title "Detail záznamu" and X close button.

**Body:** `flex-1 overflow-y-auto p-6` — same sections as before, same Czech labels, but with more space:
- Date, User (if present), Question, Answer — same content, same order
- Metadata row (TopK / Zdrojů / Doba odezvy): 3-column grid instead of `flex gap-6`
- Feedback section: Přesnost + Styl side by side in a 2-column grid, Komentář below full-width

**Props interface:** unchanged — `{ log: FeedbackLogSummary; onClose: () => void }`

No `isOpen` prop — modal is rendered conditionally by the caller.

---

### `KnowledgeBaseFeedbackPage.tsx`

- Remove the `flex` split layout wrapper from the main content area
- Main content becomes a single full-width scrollable column (`flex-1 overflow-y-auto p-6 space-y-4`)
- Remove the `{selectedLog && <div className="w-96 ...">}` right column entirely
- Add `{selectedLog && <FeedbackDetailModal log={selectedLog} onClose={handleClosePanel} />}` at the bottom of the JSX (outside the scroll container)
- Update import: `FeedbackDetailPanel` → `FeedbackDetailModal`
- `handlePageChange` and `handleParamsChange` still clear `selectedLog` — no change

---

### `FeedbackTable.tsx`

- Remove `selectedId` prop and row highlight logic (`bg-blue-50 hover:bg-blue-100` conditional)
- All rows use `hover:bg-gray-50` uniformly
- `FeedbackTableProps` interface: remove `selectedId: string | null`

---

## What Does NOT Change

- All backend code, API hooks, handlers — untouched
- All content sections and Czech labels — identical
- `ScoreDots` component — moved into the modal file (or kept as a local component)
- Pagination, filters, stats bar — untouched
- `FeedbackFilters.tsx`, `FeedbackStatsBar.tsx` — untouched

## Testing

- No new unit tests required (pure visual restructuring)
- Manual verification: click a row → modal opens; X button / backdrop click → modal closes; page/filter change → modal closes
