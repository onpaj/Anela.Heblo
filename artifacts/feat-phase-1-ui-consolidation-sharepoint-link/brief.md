# Phase 1 — UI Consolidation & SharePoint Link-Back

## Goal
All three RAG features reachable from one Marketing sidebar group. Chunk modals link back to the original SharePoint document.

## Context
- `frontend/src/components/layout/Sidebar.tsx` has a **bug**: two sections share `id: 'marketing'` (lines 141 and 316). They must be merged.
- KB lives in its own top-level `knowledgebase` group (lines 300-315). Must move under Marketing.
- Articles has no sidebar entry at all. Must be added.
- `ChunkDetailModal` and `LeafletChunkDetailModal` do not show `SourcePath` — backend responses don't include it.
- No code changes to routing (routes stay at `/knowledge-base`, `/leaflet-generator`, `/articles`).

---

## Step 1 — Fix sidebar & merge into one Marketing group

**File**: `frontend/src/components/layout/Sidebar.tsx`

### 1a. Remove icons no longer needed for knowledgebase standalone section
The `Database` and `TrendingUp` imports can be removed if unused elsewhere — check first with grep.

### 1b. Replace the `navigationSections` array entries

Remove:
- The standalone `knowledgebase` section (id `'knowledgebase'`, lines ~300-315).
- The second orphan `marketing` section (id `'marketing'`, icon `TrendingUp`, lines ~316-324) that has only `Campaigns`.

Replace the existing first `marketing` section (id `'marketing'`, icon `Megaphone`, lines ~141-150) with the consolidated version:

```typescript
{
  id: "marketing",
  name: "Marketing",
  icon: Megaphone,
  type: "section" as const,
  items: [
    { id: "marketing-calendar", name: "Kalendář", href: "/marketing/calendar" },
    { id: "leaflet-generator", name: "Generátor letáků", href: "/leaflet-generator" },
    { id: "articles", name: "Generátor článků", href: "/articles" },
    { id: "knowledge-base", name: "Poradenství (KB)", href: "/knowledge-base" },
    ...(hasRole("knowledge_base_manager") || hasRole("leaflet_manager") || hasRole("article_generator")
      ? [{ id: "marketing-feedback", name: "Feedback", href: "/marketing/feedback" }]
      : []),
  ],
},
```

> The Campaigns item that was in the second orphan marketing section: check if `/campaigns` route exists in `App.tsx`. If the route is a stub or broken, omit it; if real, add it to the new merged group. No orphan section should remain.

### 1c. Remove `TrendingUp` from lucide-react import if no longer used

Verify with grep before removing.

**Test**: `npm run build` — no TS errors. Open dev server, confirm single Marketing group expands to the right items.

---

## Step 2 — Add `/marketing/feedback` route (stub for Phase 4)

Add a minimal stub page so the sidebar link works even before Phase 4 completes.

**New file**: `frontend/src/pages/MarketingFeedbackPage.tsx`

```tsx
export default function MarketingFeedbackPage() {
  return (
    <div className="p-6">
      <h1 className="text-xl font-semibold text-gray-900">Feedback</h1>
      <p className="mt-2 text-sm text-gray-500">Přehled zpětné vazby bude dostupný po dokončení integrace.</p>
    </div>
  );
}
```

**File**: `frontend/src/App.tsx`

Add import + route:
```tsx
import MarketingFeedbackPage from "./pages/MarketingFeedbackPage";
// inside the route tree, inside <AuthGuard> + <Layout>:
<Route path="/marketing/feedback" element={<MarketingFeedbackPage />} />
```

---

## Step 3 — Add `SourcePath` to KB chunk detail

### 3a. Backend — response DTO

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailResponse.cs`

Add field:
```csharp
public string? SourcePath { get; set; }
```

(The field is already `nullable` because manual-upload chunks have a synthetic `upload/{guid}/{filename}` path that should not become a link.)

### 3b. Backend — handler

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`

In the `Handle` method, the repository call must eagerly load the document (to access `SourcePath`). The existing code already reads `chunk.Document.Filename` and `chunk.Document.IndexedAt`, which means the document is already included via navigation property. Add:

```csharp
SourcePath = chunk.Document.SourcePath,
```

### 3c. Backend — repository query

**File**: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`

Find `GetChunkByIdAsync`. Verify it uses `.Include(c => c.Document)` (or equivalent JOIN) so `chunk.Document` is populated. If it uses a raw SQL projection, add `d."SourcePath"` to the SELECT and map it.

### 3d. OpenAPI client regeneration

The TypeScript client is auto-generated on `npm run build`. The new `sourcePath?: string` field will appear in the generated `GetChunkDetailResponse` interface automatically.

---

## Step 4 — Add `SourcePath` to Leaflet chunk detail

Same pattern as Step 3.

**Files to touch**:
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailResponse.cs` — add `SourcePath?: string`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs` — add `SourcePath = chunk.Document.SourcePath`
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs` — verify `GetChunkByIdAsync` includes the document (check for `.Include(c => c.Document)`)

---

## Step 5 — Shared `useSharePointLink` hook (frontend)

**New file**: `frontend/src/api/hooks/useSharePointLink.ts`

```ts
export function getSharePointLink(sourcePath: string | null | undefined): string | null {
  if (!sourcePath) return null;
  if (sourcePath.startsWith("https://")) return sourcePath;
  return null;
}
```

A pure function is sufficient — no state needed.

---

## Step 6 — Update `ChunkDetailModal` (KB)

**File**: `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`

After the meta row (score/date), add:

```tsx
import { getSharePointLink } from '../../api/hooks/useSharePointLink';
import { ExternalLink } from 'lucide-react';

// inside the {data && (...)} block, after the meta row:
{getSharePointLink(data.sourcePath) && (
  <a
    href={getSharePointLink(data.sourcePath)!}
    target="_blank"
    rel="noopener noreferrer"
    className="inline-flex items-center gap-1 text-xs text-blue-600 hover:underline"
  >
    Otevřít v SharePoint
    <ExternalLink className="w-3 h-3" />
  </a>
)}
```

---

## Step 7 — Update `LeafletChunkDetailModal`

**File**: `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx`

Same change as Step 6 — import `getSharePointLink`, render the link below the meta row.

---

## Step 8 — i18n check

No new Czech labels added except "Otevřít v SharePoint" (hardcoded inline — acceptable for a link label). No changes needed to `frontend/src/i18n.ts`.

---

## Tests to write

### Backend unit tests
- `GetChunkDetailHandlerTests` (KB) — assert `SourcePath` is returned when document has a SharePoint URL; assert `null` for upload-prefix path.
- `GetLeafletChunkDetailHandlerTests` — same assertion.

### Frontend unit tests
- `ChunkDetailModal.test.tsx` — render with `sourcePath: "https://sharepoint.com/..."` → link is visible; render with `sourcePath: "upload/abc/file.pdf"` → link is hidden.
- `LeafletChunkDetailModal.test.tsx` — same.

---

## Verification

1. `dotnet build` — clean.
2. `dotnet format` — no changes.
3. `npm run build` — clean.
4. `npm run lint` — clean.
5. Dev server: log in → Marketing group shows: Kalendář, Generátor letáků, Generátor článků, Poradenství (KB), Feedback (for managers).
6. Click "Poradenství (KB)" → `/knowledge-base` loads, tabs work.
7. Click "Generátor článků" → `/articles` loads.
8. Open KB Dokumenty tab → click a row → ChunkDetailModal opens → "Otevřít v SharePoint" link visible for OneDrive items, hidden for manual uploads.
9. Leaflet Dokumenty tab → same.
10. `/marketing/feedback` renders the stub page.