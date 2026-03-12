# Multi-file Knowledge Base Upload — Design Spec

**Date:** 2026-03-12
**Branch:** feat/381-rag-knowledge-base
**Status:** Approved

---

## Overview

Extend the Knowledge Base upload tab to support importing multiple files at once via drag & drop or file picker. Files are uploaded sequentially using the existing single-file endpoint. No backend changes required.

---

## Goals

- Allow users to drag & drop multiple files (or select multiple via file picker) in one action
- Show per-file status (waiting, uploading, done, error) in a file list
- Upload files sequentially, one at a time
- Let users remove individual files from the queue before uploading
- Stay on the upload tab after all files are processed (don't redirect)
- Retry failed files by clicking "Nahrát vše" again

---

## Non-goals

- Parallel uploads
- New backend batch endpoint
- Upload progress percentage (no Content-Length support from the backend)
- Drag-and-drop reordering of the queue

---

## Architecture Decision

**Reuse existing single-file endpoint** (`POST /api/knowledgebase/documents/upload`) called N times sequentially from the frontend. The existing deduplication (SHA-256), extraction, chunking, and embedding pipeline works unchanged per file.

---

## Frontend Design

### Affected file

`frontend/src/components/knowledge-base/KnowledgeBaseUploadTab.tsx` — full rewrite of state and render logic.

No other files need changes.

### Props

The `onUploadSuccess: () => void` prop is **removed**. The current single-file behavior redirected to the Documents tab on success; the new behavior stays on the upload tab. The prop is unused after this change — remove it from the interface and update the caller (`KnowledgeBasePage.tsx`) to stop passing it.

### State

```typescript
const [queuedFiles, setQueuedFiles] = useState<File[]>([]);
const [fileStatuses, setFileStatuses] = useState<Record<string, 'waiting' | 'uploading' | 'done' | 'error'>>({});
const [isUploading, setIsUploading] = useState(false);
```

File identity key: `file.name` (sufficient for queue deduplication within a session).

**Important:** All upload status in the render must come from `fileStatuses` only. Do NOT use `upload.isPending` or `upload.isError` from the mutation instance — these values reflect only the last individual call and will be stale/misleading across a multi-file loop.

### UI Layout

```
┌─────────────────────────────────────┐
│  Drop zone (always visible)          │
│  Drag files here or [select]         │
│  PDF, DOCX, TXT, MD                  │
└─────────────────────────────────────┘

┌─────────────────────────────────────┐
│ 📄 dokument-receptury.pdf    ✅ Hotovo │
│ 📄 katalog-surovin.docx   Nahrávám… │
│ 📄 postupy-vyroby.txt       Čeká  ✕ │
└─────────────────────────────────────┘

[⬆ Nahrát vše (2)]  [Zrušit vše]
```

### Drop zone

- Always rendered (not replaced by the file list)
- `<input type="file" multiple accept=".pdf,.docx,.txt,.md" />`
- On drop / file change: collect all files from `FileList`, filter to accepted extensions (case-insensitive suffix match: `.pdf`, `.docx`, `.txt`, `.md` — e.g. `document.PDF` is accepted), skip files already in queue by name (case-sensitive), append remaining to `queuedFiles`
- The `accept` attribute on `<input>` only filters the OS file picker — extension filtering must also be applied manually in the `onDrop` handler since drag-and-drop bypasses it
- Visual drag-over highlight unchanged
- Drop zone remains interactive during upload — new files are added to the queue but not automatically processed

### File list

Rendered below the drop zone when `queuedFiles.length > 0`. Each row:

| Element | Behavior |
|---------|----------|
| File icon + name | Static |
| Status label | `Čeká` (gray) → `Nahrávám…` (blue) → `✅ Hotovo` (green) → `❌ Chyba` (red) |
| X button | Removes file from queue. Disabled during any active upload (`isUploading === true`) for all rows. |

Successfully uploaded files (`done`) remain visible in the list until "Zrušit vše" is clicked or the component unmounts — they are not removed one-by-one as they complete.

### Action buttons

| Button | Label | Enabled when |
|--------|-------|--------------|
| Primary | `⬆ Nahrát vše (N)` | `pendingCount > 0 && !isUploading` |
| Secondary | `Zrušit vše` | `!isUploading` — clears queue and all statuses; disabled during active upload |

Where `N = pendingCount = queuedFiles.filter(f => fileStatuses[f.name] !== 'done').length`. This excludes already-done files from the count so the label accurately reflects how many files will be processed on the next click.

### Upload logic

Use a local tracking object to avoid stale closure issues with `fileStatuses` state:

```typescript
const handleUpload = async () => {
  setIsUploading(true);

  // Snapshot which files to process (non-done) at the moment upload starts
  const filesToProcess = queuedFiles.filter(
    f => fileStatuses[f.name] !== 'done'
  );

  // Track outcomes locally to avoid stale state closures
  const outcomes: Record<string, 'done' | 'error'> = {};

  for (const file of filesToProcess) {
    setFileStatuses(prev => ({ ...prev, [file.name]: 'uploading' }));
    try {
      await upload.mutateAsync(file);
      outcomes[file.name] = 'done';
      setFileStatuses(prev => ({ ...prev, [file.name]: 'done' }));
    } catch {
      outcomes[file.name] = 'error';
      setFileStatuses(prev => ({ ...prev, [file.name]: 'error' }));
    }
  }

  setIsUploading(false);

  // Keep failed files in the queue for retry; remove done ones
  setQueuedFiles(prev =>
    prev.filter(f => outcomes[f.name] !== 'done')
  );

  // Note: useUploadKnowledgeBaseDocumentMutation already invalidates
  // 'knowledgebase-documents' in its onSuccess callback (per file).
  // No additional invalidateQueries call needed here.
};
```

**Query invalidation:** The existing `useUploadKnowledgeBaseDocumentMutation` hook already calls `queryClient.invalidateQueries` in its `onSuccess` for each successful file. Do NOT add a second `invalidateQueries` call after the loop — it would cause redundant refetches (N+1 for an N-file batch).

After the loop: failed files remain in the queue. User can click "Nahrát vše" again to retry only failed/waiting files — `done` files are excluded by the `filesToProcess` filter.

---

## Error handling

- Per-file errors shown inline as `❌ Chyba` — other files continue unaffected
- Network/server errors on a file set that file to `error` and the loop continues to the next
- Duplicate file names already in queue are silently skipped on drop
- Files with unsupported or missing extensions are filtered out silently on drop (both from file picker and drag & drop)

---

## Testing

### Frontend unit tests (Jest + React Testing Library)

New test file: `frontend/src/components/knowledge-base/__tests__/KnowledgeBaseUploadTab.test.tsx`

Mock `useUploadKnowledgeBaseDocumentMutation` to control success/failure per call. The mock should support per-call control (e.g. first call succeeds, second fails) to test partial failure scenarios.

**Test cases:**

| # | Scenario | Assert |
|---|----------|--------|
| 1 | Drop 3 valid files | All 3 appear in list with "Čeká" status |
| 2 | Drop file with unsupported extension | File not added to queue |
| 3 | Drop file with uppercase extension (e.g. `.PDF`) | File accepted and added to queue |
| 4 | Drop duplicate filename | Second drop ignored, list length unchanged |
| 5 | Remove file before upload | File removed from list; queue length decreases |
| 6 | Click "Nahrát vše" — all succeed | Each file transitions Čeká → Nahrávám… → ✅ Hotovo; button disabled during upload; done files removed from queue after loop |
| 7 | Click "Nahrát vše" — one file fails | Failed file shows ❌ Chyba; other files complete normally; failed file remains in queue |
| 8 | Click "Nahrát vše" again after partial failure | Only failed file re-processed; already-done file not passed to mutateAsync |
| 9 | Click "Zrušit vše" | Queue cleared, statuses cleared, file list hidden |
| 10 | Drop zone always visible | Drop zone rendered before files added, while files are queued, and after upload completes |
| 11 | Upload button label shows pending count | "Nahrát vše (3)" for 3 waiting files; after 1 succeeds, label shows "Nahrát vše (2)" not "(3)" |
| 12 | `onUploadSuccess` prop removed | Caller (`KnowledgeBasePage`) compiles without passing the prop |
| 13 | X buttons disabled during upload | All X buttons disabled while `isUploading` is true |
| 14 | "Zrušit vše" disabled during upload | Button has `disabled` attribute while `isUploading` is true |
| 15 | New files dropped during active upload | New files appear in list with "Čeká" but are not auto-processed |

### Manual smoke test checklist

- [ ] Drop 3 files → all appear with "Čeká" status
- [ ] Remove one before upload → removed from list
- [ ] Upload → files process one by one, status updates correctly
- [ ] Simulate error → others continue, failed file stays for retry
- [ ] Retry → only failed file re-processes
- [ ] Drop zone accepts new files during upload
- [ ] "Zrušit vše" clears queue
- [ ] After upload: stay on upload tab, Documents tab reflects newly indexed files
