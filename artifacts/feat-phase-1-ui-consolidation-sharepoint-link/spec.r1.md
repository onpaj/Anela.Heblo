# Specification: Phase 1 — UI Consolidation & SharePoint Link-Back

## Summary
Consolidate the three RAG-related features (Knowledge Base, Leaflet Generator, Article Generator) under a single Marketing sidebar group, fixing an existing duplicate-id bug. Surface a clickable SharePoint link in chunk detail modals for both KB and Leaflet by propagating the document `SourcePath` from backend through to the UI.

## Background
The application currently has three knowledge-base-style features (Knowledge Base / "Poradenství", Leaflet Generator, Article Generator) that share a common RAG architecture but are surfaced inconsistently in the sidebar:

- `Sidebar.tsx` defines two sections with the same `id: 'marketing'` (lines ~141 and ~316), which is a latent bug (React key collisions, ambiguous active-state behaviour).
- Knowledge Base lives in its own top-level `knowledgebase` group (lines ~300-315) instead of under Marketing.
- Article Generator has no sidebar entry at all and is only reachable by direct URL.
- Chunk detail modals (`ChunkDetailModal` for KB, `LeafletChunkDetailModal` for Leaflet) cannot link back to the originating SharePoint document because the GetChunkDetail response DTOs do not carry `SourcePath`. Documents have two flavours of `SourcePath`: real SharePoint URLs (`https://...`) for OneDrive/SharePoint-sourced documents, and a synthetic `upload/{guid}/{filename}` prefix for manually uploaded files. Only the former should render as a link.

This phase is purely consolidation and link-back — no routing changes, no new business logic. It also adds a stub `/marketing/feedback` page so the sidebar entry resolves cleanly until Phase 4 implements the real feedback view.

## Functional Requirements

### FR-1: Consolidated Marketing sidebar group
Merge all marketing-adjacent navigation entries into a single `marketing` section in `frontend/src/components/layout/Sidebar.tsx`. The merged section must contain, in this exact order:
1. Kalendář → `/marketing/calendar`
2. Generátor letáků → `/leaflet-generator`
3. Generátor článků → `/articles`
4. Poradenství (KB) → `/knowledge-base`
5. Feedback → `/marketing/feedback` (only when user has at least one of `knowledge_base_manager`, `leaflet_manager`, `article_generator` roles)

The previously standalone `knowledgebase` section and the orphan duplicate `marketing` section (the one icon'd with `TrendingUp` containing only `Campaigns`) must be removed. The merged section uses the `Megaphone` icon.

**Acceptance criteria:**
- Only one `navigationSections` entry has `id: 'marketing'`.
- No entry has `id: 'knowledgebase'`.
- The `Megaphone` icon is used; `TrendingUp` and `Database` imports are removed from `lucide-react` if grep confirms no other in-file usage.
- Sidebar renders the five items above in order; the Feedback item is conditional on the role check shown in the brief.
- `npm run build` and `npm run lint` are clean.
- Visual check: sidebar shows a single Marketing group expanding to the right items.

### FR-2: Disposition of `Campaigns` item
The orphan duplicate marketing section currently contains a `Campaigns` item. Before removal, verify whether `/campaigns` is a real route in `frontend/src/App.tsx`:
- If a real, working route exists → include `Campaigns` as an additional item in the merged Marketing group (placement after `Feedback` or before `Kalendář` is at developer discretion; pick the natural marketing workflow order).
- If the route is absent, a stub, or otherwise broken → omit the item entirely.

No orphan section may remain regardless of outcome.

**Acceptance criteria:**
- Decision is documented in the PR description with the specific App.tsx evidence (route present yes/no, file:line).
- No `Campaigns` link points to a missing route.

### FR-3: Marketing Feedback stub page
Add a minimal stub page so the sidebar `Feedback` link resolves before Phase 4 is implemented.

- New file: `frontend/src/pages/MarketingFeedbackPage.tsx` (default export, body exactly as specified in the brief: heading "Feedback", subtext "Přehled zpětné vazby bude dostupný po dokončení integrace.", standard `p-6` page wrapper).
- Register `/marketing/feedback` in `frontend/src/App.tsx` inside the existing `<AuthGuard>` + `<Layout>` route tree.

**Acceptance criteria:**
- Navigating to `/marketing/feedback` while authenticated renders the stub.
- The route is gated by the same auth guard as sibling marketing routes.
- No console errors on render.

### FR-4: Backend — KB GetChunkDetail returns `SourcePath`
Add `SourcePath` to the KB chunk detail pipeline.

- `GetChunkDetailResponse.cs`: add `public string? SourcePath { get; set; }` (nullable, since synthetic upload paths must surface but should be hidden in UI per FR-7).
- `GetChunkDetailHandler.cs`: populate `SourcePath = chunk.Document.SourcePath` when constructing the response.
- `KnowledgeBaseRepository.GetChunkByIdAsync`: ensure `chunk.Document` is loaded (`.Include(c => c.Document)` for EF; if the method uses a raw SQL projection, add `d."SourcePath"` to the SELECT and map it).

**Acceptance criteria:**
- For a chunk whose document has a SharePoint URL, the response contains that URL in `sourcePath`.
- For a chunk whose document has a synthetic `upload/{guid}/{filename}` path, the response contains that synthetic path verbatim (UI handles hiding — see FR-7).
- For a chunk with no document association (if such a case exists), `sourcePath` is `null` and the handler does not throw.
- `dotnet build` and `dotnet format` are clean.

### FR-5: Backend — Leaflet GetLeafletChunkDetail returns `SourcePath`
Mirror FR-4 for the Leaflet feature:
- `GetLeafletChunkDetailResponse.cs`: add `public string? SourcePath { get; set; }`.
- `GetLeafletChunkDetailHandler.cs`: assign `SourcePath = chunk.Document.SourcePath`.
- `LeafletRepository.GetChunkByIdAsync`: verify document include or extend SQL projection identically to FR-4.

**Acceptance criteria:** same as FR-4, applied to the Leaflet handler.

### FR-6: Shared `getSharePointLink` helper
New file `frontend/src/api/hooks/useSharePointLink.ts` exporting a pure function:

```ts
export function getSharePointLink(sourcePath: string | null | undefined): string | null {
  if (!sourcePath) return null;
  if (sourcePath.startsWith("https://")) return sourcePath;
  return null;
}
```

Used by both chunk detail modals.

**Acceptance criteria:**
- Returns `null` for `null`, `undefined`, empty string, and any value not starting with `https://` (including the `upload/...` prefix).
- Returns the input verbatim when it starts with `https://`.
- Function has no side effects, no React hooks, no state.

### FR-7: KB ChunkDetailModal renders SharePoint link
In `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`, after the existing meta row (score/date), conditionally render an `<a>` tag that opens the SharePoint URL in a new tab when `getSharePointLink(data.sourcePath)` returns a non-null value.

- Link label (hardcoded): `Otevřít v SharePoint`
- Trailing `ExternalLink` icon from `lucide-react` (`w-3 h-3`)
- Classes: `inline-flex items-center gap-1 text-xs text-blue-600 hover:underline`
- Anchor attributes: `target="_blank" rel="noopener noreferrer"`

**Acceptance criteria:**
- Link is visible when `sourcePath` is a `https://...` URL.
- Link is hidden when `sourcePath` is `null`, `undefined`, or starts with `upload/`.
- Clicking opens a new tab to the SharePoint URL; no in-app navigation occurs.
- No layout regression in the modal for either branch.

### FR-8: Leaflet LeafletChunkDetailModal renders SharePoint link
Apply the same change as FR-7 in `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx`.

**Acceptance criteria:** same as FR-7, in the Leaflet modal.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. The added `SourcePath` field is a small string already loaded as part of the `Document` entity (the handlers already read `Filename` and `IndexedAt` from the same navigation property). No new round trips, no additional joins beyond what already exists.

### NFR-2: Security
- The SharePoint URL is rendered as a normal anchor with `rel="noopener noreferrer"` to prevent reverse-tabnabbing.
- No new auth surface is introduced; access to chunk detail data continues to use existing endpoint authorisation.
- `SourcePath` is already persisted server-side and exposed for indexing — no new data is leaving the database.
- The role check for the Feedback sidebar item uses the existing `hasRole` helper; no new role logic is introduced.

### NFR-3: Accessibility
- The SharePoint link is keyboard-focusable (default anchor behaviour) and has visible text content; the icon is decorative.
- The Feedback stub page uses semantic HTML (`<h1>`, `<p>`).

### NFR-4: Internationalisation
The label "Otevřít v SharePoint" is hardcoded inline. Per the brief, this is acceptable for Phase 1 and `frontend/src/i18n.ts` is not modified. The Feedback stub copy is also Czech and inline. (See Open Questions for the i18n trade-off.)

### NFR-5: Backwards compatibility
Adding a nullable `SourcePath` field to two response DTOs is additive — existing TypeScript consumers compile without changes once the OpenAPI client regenerates on `npm run build`. No persistence migrations are required (`Document.SourcePath` already exists).

## Data Model

No schema changes. Relevant existing entities:

- `KnowledgeBaseDocument` — has `SourcePath` (string, nullable), `Filename`, `IndexedAt`.
- `KnowledgeBaseChunk` — has `Document` navigation property to `KnowledgeBaseDocument`.
- `LeafletDocument` and `LeafletChunk` — analogous shape on the Leaflet side.

`SourcePath` semantics (existing convention):
- `https://...` → real SharePoint/OneDrive URL.
- `upload/{guid}/{filename}` → synthetic identifier for manually uploaded files. **Not** a clickable link.

## API / Interface Design

### Backend API contract changes (additive)
- `GET /api/knowledge-base/chunks/{id}` — response gains optional `sourcePath: string | null`.
- `GET /api/leaflet/chunks/{id}` — response gains optional `sourcePath: string | null`.

(Exact route paths follow the existing controllers; no new endpoints.)

### Frontend OpenAPI client
Auto-regenerated on `npm run build`. The generated `GetChunkDetailResponse` and `GetLeafletChunkDetailResponse` interfaces gain `sourcePath?: string`. No manual edits to generated files.

### Sidebar UI flow
Single `Marketing` group → expanded items: Kalendář, Generátor letáků, Generátor článků, Poradenství (KB), [Feedback for managers].

### Chunk modal UI flow
Open Documents tab → click a chunk row → modal opens → meta row (score/date) → optional SharePoint link row → existing chunk text/preview content.

## Dependencies

- **lucide-react** — already a dependency; uses `Megaphone` (existing) and `ExternalLink` (existing) icons.
- **OpenAPI client generator** — already wired into `npm run build`; no new tooling.
- **Existing roles** (`knowledge_base_manager`, `leaflet_manager`, `article_generator`) — already defined and queryable via the existing `hasRole` helper used in `Sidebar.tsx`.
- **Existing routes** (`/knowledge-base`, `/leaflet-generator`, `/articles`, `/marketing/calendar`) — present and unchanged.
- **Existing entity navigation** — `Chunk.Document` is already loaded by current handlers (which read `Filename`, `IndexedAt`); confirming the include in the repository is a verification step, not a new dependency.

## Test Plan

### Backend unit tests
- **`GetChunkDetailHandlerTests` (KB)**
  - Returns `SourcePath` populated when the document has a SharePoint URL.
  - Returns the synthetic `upload/...` path verbatim when the document was manually uploaded.
  - Returns `null` `SourcePath` if the document has no source path stored.
- **`GetLeafletChunkDetailHandlerTests`** — same three cases as above.

### Frontend unit tests
- **`ChunkDetailModal.test.tsx`**
  - Given `sourcePath: "https://sharepoint.com/foo"` → "Otevřít v SharePoint" link is visible with that `href`, opens in new tab.
  - Given `sourcePath: "upload/abc/file.pdf"` → link is not in the DOM.
  - Given `sourcePath: null` / `undefined` → link is not in the DOM.
- **`LeafletChunkDetailModal.test.tsx`** — same three cases.
- **`useSharePointLink.test.ts`** — direct unit tests for `getSharePointLink` covering all four input categories (null, undefined, https-prefixed, upload-prefixed).

### Verification (manual / E2E-equivalent smoke)
1. `dotnet build` clean.
2. `dotnet format` produces no changes.
3. `npm run build` clean.
4. `npm run lint` clean.
5. Dev server, authenticated as a manager: Marketing group expands to the five items in the specified order.
6. Navigate to each of `/knowledge-base`, `/leaflet-generator`, `/articles`, `/marketing/calendar`, `/marketing/feedback` — each loads without console error.
7. KB Dokumenty tab → open a chunk for a SharePoint document → SharePoint link is present and opens a new tab.
8. KB Dokumenty tab → open a chunk for a manually uploaded document → link is absent.
9. Leaflet Dokumenty tab → repeat steps 7–8.
10. Sign in as a non-manager (no relevant roles) → Feedback item is hidden; the four other Marketing items are visible.

## Out of Scope

- Any changes to routing paths (`/knowledge-base`, `/leaflet-generator`, `/articles` stay).
- The real implementation of `/marketing/feedback` (Phase 4).
- Any change to how `SourcePath` is persisted, ingested, or generated.
- i18n extraction of the new inline labels.
- Restyling or reorganising the chunk detail modal beyond inserting the link row.
- Permissions changes; only the existing `hasRole` checks are reused.
- Addition or removal of any feature in Knowledge Base, Leaflet, or Articles beyond surfacing them in the sidebar.
- Backfill or normalisation of `SourcePath` values (e.g. converting legacy paths into URLs).

## Open Questions

None.

## Status: COMPLETE