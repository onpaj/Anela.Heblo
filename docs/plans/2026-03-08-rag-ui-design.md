# RAG Knowledge Base UI Design

**Date:** 2026-03-08
**Status:** Approved

## Summary

Add a top-level "Knowledge Base" page to the application with three tabs: merged search/ask, document list, and file upload. Upload and delete operations are gated by the `KnowledgeBase.Upload` Entra ID custom claim.

---

## Navigation

- New sidebar item: **Znalostní báze** (visible to all authenticated users)
- Route: `/knowledge-base`
- Three tabs: `Hledat` | `Dokumenty` | `Nahrát soubor`
- `Nahrát soubor` tab is only rendered for users with `KnowledgeBase.Upload` claim

---

## Tab 1: Hledat (merged Search + Ask AI)

Single question/query input with a submit button. On submit:

1. Calls `POST /api/knowledgebase/ask` (RAG pipeline: vector search → Claude synthesis)
2. Displays AI-generated prose answer
3. Collapsible "Zdroje" accordion below the answer, showing ranked chunks (filename + similarity score badge + excerpt)

The separate `SearchTab` and `AskTab` components are merged into one `KnowledgeBaseSearchAskTab.tsx`.

---

## Tab 2: Dokumenty

Table of all indexed documents with columns: filename, status badge, upload date, delete action.

- **Status badges:** `indexed` (green), `processing` (amber with spinner), `failed` (red)
- **Delete button:** visible only to users with `KnowledgeBase.Upload` claim; shows confirmation dialog before deletion
- Calls `DELETE /api/knowledgebase/documents/{id}` on confirm
- Empty state when no documents exist

---

## Tab 3: Nahrát soubor

Only rendered for users with `KnowledgeBase.Upload` claim.

- Drag-and-drop zone + "Vybrat soubor" browse button (no auto-upload on drop/select)
- Supported formats displayed: PDF, DOCX, TXT, MD
- After file selection: filename preview, upload button, cancel button
- Progress indicator during upload
- On success: toast notification + redirect to Dokumenty tab
- Calls `POST /api/knowledgebase/documents/upload` (multipart/form-data)

---

## Claim-Based Authorization

### Backend

- Upload endpoint `POST /api/knowledgebase/documents/upload` decorated with `[Authorize(Policy = "KnowledgeBaseUpload")]`
- Delete endpoint `DELETE /api/knowledgebase/documents/{id}` decorated with `[Authorize(Policy = "KnowledgeBaseUpload")]`
- Policy registered in `Program.cs`: requires claim `KnowledgeBase.Upload` with any non-empty value
- All other endpoints remain `[Authorize]` only (all authenticated users)

### Frontend

- Read claim from MSAL account's `idTokenClaims` object
- Custom hook `useKnowledgeBaseUploadPermission()` returns boolean
- `Nahrát soubor` tab is conditionally rendered (not just disabled)
- Delete button in Dokumenty tab is conditionally rendered

---

## Backend Changes Required

| Change | Details |
|--------|---------|
| New endpoint | `POST /api/knowledgebase/documents/upload` — multipart, `KnowledgeBaseUpload` policy |
| New endpoint | `DELETE /api/knowledgebase/documents/{id}` — `KnowledgeBaseUpload` policy |
| New handler | `UploadDocumentHandler` — saves file, extracts text, chunks, embeds, stores |
| Auth policy | Register `KnowledgeBaseUpload` policy in `Program.cs` |

## Frontend Changes Required

| Change | Details |
|--------|---------|
| Route | Add `/knowledge-base` to `App.tsx` |
| Sidebar | Add nav item to `Sidebar.tsx` |
| New page | `KnowledgeBasePage.tsx` — tab container |
| Merge tabs | `KnowledgeBaseSearchAskTab.tsx` (replaces separate Search/Ask tabs) |
| Update | `KnowledgeBaseDocumentsTab.tsx` — add conditional delete button |
| New tab | `KnowledgeBaseUploadTab.tsx` — drag-drop + browse + progress |
| New hook | `useUploadKnowledgeBaseDocument` mutation hook |
| New hook | `useKnowledgeBaseUploadPermission` — reads MSAL claim, returns boolean |
| Regenerate | TypeScript API client after adding upload/delete endpoints |
