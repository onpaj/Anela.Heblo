# KB Feedback Detail Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fixed right-side detail panel on the KB Feedback Browser with a centered modal dialog.

**Architecture:** Three files change — the panel component is converted to a modal, the table loses its selection highlight, and the page swaps the side-column layout for a full-width layout with the modal rendered at root level. No backend changes.

**Tech Stack:** React, TypeScript, Tailwind CSS

---

## File Map

| Action | File |
|--------|------|
| Replace | `frontend/src/components/knowledge-base/FeedbackDetailPanel.tsx` → deleted, replaced by `FeedbackDetailModal.tsx` |
| Create | `frontend/src/components/knowledge-base/FeedbackDetailModal.tsx` |
| Modify | `frontend/src/components/knowledge-base/FeedbackTable.tsx` |
| Modify | `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx` |

---

## Task 1: Create `FeedbackDetailModal.tsx`

**Files:**
- Create: `frontend/src/components/knowledge-base/FeedbackDetailModal.tsx`

- [ ] **Step 1: Create the modal component**

Create `frontend/src/components/knowledge-base/FeedbackDetailModal.tsx` with the following content:

```tsx
import React from 'react';
import { X } from 'lucide-react';
import { FeedbackLogSummary } from '../../api/hooks/useKnowledgeBase';

interface FeedbackDetailModalProps {
  log: FeedbackLogSummary;
  onClose: () => void;
}

const ScoreDots: React.FC<{ score: number | null; max?: number }> = ({ score, max = 5 }) => {
  if (score === null) return <span className="text-gray-400 text-sm">–</span>;
  return (
    <span className="flex gap-1 items-center">
      {Array.from({ length: max }, (_, i) => (
        <span
          key={i}
          className={`inline-block w-3 h-3 rounded-full ${i < score ? 'bg-blue-500' : 'bg-gray-200'}`}
        />
      ))}
      <span className="ml-1 text-sm text-gray-700">{score}/{max}</span>
    </span>
  );
};

const FeedbackDetailModal: React.FC<FeedbackDetailModalProps> = ({ log, onClose }) => {
  const formatDate = (iso: string) =>
    new Date(iso).toLocaleString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-3xl w-full mx-4 max-h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 flex-shrink-0">
          <h2 className="text-base font-semibold text-gray-800">Detail záznamu</h2>
          <button
            onClick={onClose}
            className="p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100"
            aria-label="Zavřít"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-5 text-sm">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Datum</p>
              <p className="text-gray-900">{formatDate(log.createdAt)}</p>
            </div>
            {log.userId && (
              <div>
                <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Uživatel</p>
                <p className="text-gray-900 break-all">{log.userId}</p>
              </div>
            )}
          </div>

          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Dotaz</p>
            <p className="text-gray-900 whitespace-pre-wrap">{log.question}</p>
          </div>

          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Odpověď</p>
            <p className="text-gray-700 whitespace-pre-wrap leading-relaxed">{log.answer}</p>
          </div>

          <div className="grid grid-cols-3 gap-4">
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">TopK</p>
              <p className="text-gray-900">{log.topK}</p>
            </div>
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Zdrojů</p>
              <p className="text-gray-900">{log.sourceCount}</p>
            </div>
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Doba odezvy</p>
              <p className="text-gray-900">{log.durationMs} ms</p>
            </div>
          </div>

          {log.hasFeedback && (
            <div className="border-t border-gray-100 pt-5 space-y-4">
              <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Feedback</p>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <p className="text-xs text-gray-500 mb-1">Přesnost</p>
                  <ScoreDots score={log.precisionScore} />
                </div>
                <div>
                  <p className="text-xs text-gray-500 mb-1">Styl</p>
                  <ScoreDots score={log.styleScore} />
                </div>
              </div>

              {log.feedbackComment && (
                <div>
                  <p className="text-xs text-gray-500 mb-1">Komentář</p>
                  <p className="text-gray-700 whitespace-pre-wrap">{log.feedbackComment}</p>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default FeedbackDetailModal;
```

- [ ] **Step 2: Verify TypeScript compiles**

Run from the repo root:
```bash
cd frontend && npx tsc --noEmit
```
Expected: no errors related to `FeedbackDetailModal.tsx`.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/knowledge-base/FeedbackDetailModal.tsx
git commit -m "feat(#432): add FeedbackDetailModal component"
```

---

## Task 2: Update `FeedbackTable` — remove selection highlight

**Files:**
- Modify: `frontend/src/components/knowledge-base/FeedbackTable.tsx`

- [ ] **Step 1: Remove `selectedId` from the props interface**

In `FeedbackTable.tsx`, change the interface from:
```tsx
interface FeedbackTableProps {
  logs: FeedbackLogSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  selectedId: string | null;
  onSelect: (log: FeedbackLogSummary) => void;
  onPageChange: (page: number) => void;
}
```
to:
```tsx
interface FeedbackTableProps {
  logs: FeedbackLogSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  onSelect: (log: FeedbackLogSummary) => void;
  onPageChange: (page: number) => void;
}
```

- [ ] **Step 2: Remove `selectedId` from the destructured props**

Change the component signature from:
```tsx
const FeedbackTable: React.FC<FeedbackTableProps> = ({
  logs,
  totalCount,
  pageNumber,
  pageSize,
  totalPages,
  selectedId,
  onSelect,
  onPageChange,
}) => {
```
to:
```tsx
const FeedbackTable: React.FC<FeedbackTableProps> = ({
  logs,
  totalCount,
  pageNumber,
  pageSize,
  totalPages,
  onSelect,
  onPageChange,
}) => {
```

- [ ] **Step 3: Remove the conditional row highlight class**

Find the `<tr>` element in the `tbody` map and replace:
```tsx
className={`cursor-pointer transition-colors ${
  log.id === selectedId
    ? 'bg-blue-50 hover:bg-blue-100'
    : 'hover:bg-gray-50'
}`}
```
with:
```tsx
className="cursor-pointer transition-colors hover:bg-gray-50"
```

- [ ] **Step 4: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit
```
Expected: TypeScript error on `FeedbackTable` usage in `KnowledgeBaseFeedbackPage.tsx` because `selectedId` prop is still passed there — that's fine, fix in Task 3.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/knowledge-base/FeedbackTable.tsx
git commit -m "refactor(#432): remove row selection highlight from FeedbackTable"
```

---

## Task 3: Update `KnowledgeBaseFeedbackPage` — swap panel for modal

**Files:**
- Modify: `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx`

- [ ] **Step 1: Update the import**

Change:
```tsx
import FeedbackDetailPanel from '../components/knowledge-base/FeedbackDetailPanel';
```
to:
```tsx
import FeedbackDetailModal from '../components/knowledge-base/FeedbackDetailModal';
```

- [ ] **Step 2: Replace the full page layout JSX**

Replace the entire `return (...)` block with:
```tsx
  return (
    <div className="flex flex-col h-full">
      {/* Page header */}
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      </div>

      {/* Main content */}
      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        {/* Stats */}
        {data?.stats && <FeedbackStatsBar stats={data.stats} />}

        {/* Filters */}
        <FeedbackFilters params={params} onParamsChange={handleParamsChange} />

        {/* Loading / error / table */}
        {isLoading && (
          <div className="flex items-center justify-center h-32 text-sm text-gray-500">
            Načítám…
          </div>
        )}

        {isError && (
          <div className="flex items-center justify-center h-32 text-sm text-red-600">
            Nepodařilo se načíst záznamy. Zkuste to znovu.
          </div>
        )}

        {data && !isLoading && (
          <FeedbackTable
            logs={data.logs}
            totalCount={data.totalCount}
            pageNumber={data.pageNumber}
            pageSize={data.pageSize}
            totalPages={data.totalPages}
            onSelect={handleSelect}
            onPageChange={handlePageChange}
          />
        )}
      </div>

      {/* Detail modal */}
      {selectedLog && (
        <FeedbackDetailModal log={selectedLog} onClose={handleClosePanel} />
      )}
    </div>
  );
```

- [ ] **Step 3: Verify TypeScript compiles cleanly**

```bash
cd frontend && npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 4: Verify frontend builds**

```bash
cd frontend && npm run build
```
Expected: build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/KnowledgeBaseFeedbackPage.tsx
git commit -m "feat(#432): show feedback detail in modal instead of side panel"
```

---

## Task 4: Delete old panel file

**Files:**
- Delete: `frontend/src/components/knowledge-base/FeedbackDetailPanel.tsx`

- [ ] **Step 1: Delete the file**

```bash
git rm frontend/src/components/knowledge-base/FeedbackDetailPanel.tsx
```

- [ ] **Step 2: Verify nothing imports it**

```bash
grep -r "FeedbackDetailPanel" frontend/src/
```
Expected: no output (zero references).

- [ ] **Step 3: Verify TypeScript still compiles**

```bash
cd frontend && npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git commit -m "chore(#432): remove FeedbackDetailPanel (replaced by FeedbackDetailModal)"
```

---

## Manual Verification Checklist

After all tasks are complete:

- [ ] Navigate to `/feedback` in the app
- [ ] Click any table row → modal opens over the full page with a dark backdrop
- [ ] Modal shows: date, user (if present), question, answer, TopK/Zdrojů/Doba odezvy, feedback scores + comment (if present)
- [ ] Click the X button → modal closes, table remains
- [ ] Change page (pagination) → modal closes
- [ ] Change a filter → modal closes
- [ ] The table no longer shows a blue highlight on any row
- [ ] The table now uses full page width (no split layout)
