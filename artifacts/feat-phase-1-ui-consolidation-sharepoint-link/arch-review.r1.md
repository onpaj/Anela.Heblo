I've gathered enough context. Let me write the architecture review.

# Architecture Review: Phase 1 — UI Consolidation & SharePoint Link-Back

## Architectural Fit Assessment

This feature is a **surgical, additive change** that fits cleanly into existing patterns:

- **Backend**: MediatR Vertical Slice handlers under `Features/{Module}/UseCases/{UseCase}/`. The change is purely additive — extend two existing response DTOs, populate one new field in two handlers. No interface changes, no new endpoints, no new persistence types. The `Document` navigation property is already eagerly loaded in both `GetChunkByIdAsync` implementations (verified: `KnowledgeBaseRepository.cs:204-209` and `LeafletRepository.cs:218-223` both call `.Include(c => c.Document)`).
- **Frontend**: A small render addition to two modals + a stateless utility helper + sidebar restructure + one stub page.
- **Integration risk**: Low. The OpenAPI client regenerates on `npm run build`; new optional field is non-breaking.

**Significant spec drift from current code (must be reconciled before implementation):**

1. There is **no duplicate `marketing` section** in `Sidebar.tsx`. The file has exactly one section with `id: 'marketing'` (lines 140–149), and a separate `id: 'knowledgebase'` section (lines 299–314). The "duplicate-id bug" described in the brief and FR-1 does not exist in the current source.
2. **No `TrendingUp` import** exists in `Sidebar.tsx` (verified: lines 1–22 show only `LayoutDashboard, Package, ShoppingCart, ChevronDown, ChevronRight, PanelLeftClose, PanelLeftOpen, Menu, DollarSign, Cog, Truck, Bot, Newspaper, Users, ExternalLink, FileText, Database, Megaphone`).
3. **No `Campaigns` item** in the sidebar; FR-2 has nothing to act on.
4. **No standalone `GetChunkDetailResponse.cs` file**. The response class is co-located in `GetChunkDetailRequest.cs:12-26` (KB) and `GetLeafletChunkDetailRequest.cs:11-24` (Leaflet). Spec instructions referring to `GetChunkDetailResponse.cs` and `GetLeafletChunkDetailResponse.cs` must edit the request files instead.
5. **`SourcePath` on the domain entities is `string`, not nullable** (`KnowledgeBaseDocument.cs:7` and `LeafletDocument.cs:7`, both `string SourcePath { get; set; } = string.Empty;`). The "no source path" sentinel is therefore an empty string, not null. The DTO field can still be declared `string?` for defensive nullability, but the handler assignment will never actually produce `null` from current data.

## Proposed Architecture

### Component Overview

```
Frontend
├─ components/layout/Sidebar.tsx          ← consolidate marketing items
├─ pages/MarketingFeedbackPage.tsx        ← NEW stub
├─ utils/sharepointLink.ts                ← NEW pure helper (renamed from spec)
├─ components/knowledge-base/
│   └─ ChunkDetailModal.tsx               ← render link
└─ features/leaflet-generator/
    └─ LeafletChunkDetailModal.tsx        ← render link
App.tsx                                    ← register /marketing/feedback

Backend
├─ Features/KnowledgeBase/UseCases/GetChunkDetail/
│   ├─ GetChunkDetailRequest.cs           ← extend Response (same file)
│   └─ GetChunkDetailHandler.cs           ← assign SourcePath
└─ Features/Leaflet/UseCases/GetLeafletChunkDetail/
    ├─ GetLeafletChunkDetailRequest.cs    ← extend Response (same file)
    └─ GetLeafletChunkDetailHandler.cs    ← assign SourcePath
```

No changes to repositories, persistence, or domain layer. The eager-load and DB column already exist.

### Key Design Decisions

#### Decision 1: Where to place the SharePoint link helper

**Options considered:**
- A. `frontend/src/api/hooks/useSharePointLink.ts` exporting `getSharePointLink` (per spec).
- B. `frontend/src/utils/sharepointLink.ts` exporting `getSharePointLink`.
- C. Inline the four-line check inside each modal.

**Chosen approach:** **B — `frontend/src/utils/sharepointLink.ts`**.

**Rationale:** The function is a pure synchronous predicate with no React state, no hooks, no API calls. Filing it under `api/hooks/` and prefixing it `use…` violates the established naming convention (`hasRole`, `useDebounce`, `useChunkDetailQuery`) where `use*` denotes a React hook and `api/hooks/` houses TanStack-Query wrappers (see `useKnowledgeBase.ts`, `useLeaflet.ts`). C is rejected because both modals need the same logic and a tested centralized predicate is cheap. The brief proposes A; that location is misleading and I recommend amending the spec.

#### Decision 2: DTO nullability of `SourcePath`

**Options considered:**
- A. `public string SourcePath { get; set; } = string.Empty;` — match the entity.
- B. `public string? SourcePath { get; set; }` — defensive (per spec).

**Chosen approach:** **B — `string?`**.

**Rationale:** The OpenAPI generator emits `sourcePath?: string` on the TS side regardless, and the frontend helper treats null/undefined/empty/upload-prefix all as "no link". B keeps the API contract honest about possible absence and gives flexibility if a future ingestion path leaves `SourcePath` unset. The handler assignment `SourcePath = chunk.Document.SourcePath` will pass empty strings through; the helper handles them correctly. No transformation needed.

#### Decision 3: Sidebar consolidation strategy

**Options considered:**
- A. Treat the spec's "remove duplicate marketing section" task literally — write code to delete a section that doesn't exist.
- B. Reinterpret the goal as: merge the existing single `marketing` section with the existing `knowledgebase` section, drop FR-2, and add Articles.

**Chosen approach:** **B**.

**Rationale:** The brief's premise ("two sections share id: 'marketing'") is stale. The actual goal — surfacing all three RAG features under one Marketing group — is achievable by extending the existing `marketing` section and removing the `knowledgebase` section. Spec amendment captures this.

#### Decision 4: Fate of `/knowledge-base/feedback` and `KnowledgeBaseFeedbackPage.tsx`

**Options considered:**
- A. Leave the existing route registered but orphaned (no sidebar link points to it) until Phase 4.
- B. Delete the route and the page now.
- C. Redirect `/knowledge-base/feedback` → `/marketing/feedback`.

**Chosen approach:** **A — leave it untouched**.

**Rationale:** "Surgical changes" rule per project CLAUDE.md. Phase 1 is consolidation + link-back; deleting an unrelated page is out of scope. The orphan route is harmless (still gated by `AuthGuard`); Phase 4 will collapse the two feedback views into one.

## Implementation Guidance

### Directory / Module Structure

**New files:**
- `frontend/src/utils/sharepointLink.ts` — pure helper.
- `frontend/src/utils/sharepointLink.test.ts` — unit tests.
- `frontend/src/pages/MarketingFeedbackPage.tsx` — stub page.

**Modified files:**
- `frontend/src/components/layout/Sidebar.tsx` — extend marketing section, remove knowledgebase section, drop unused `Database` icon if grep confirms no other usage.
- `frontend/src/App.tsx` — import + register `/marketing/feedback` route inside the existing `<AuthGuard>`/`<Layout>` block.
- `frontend/src/components/knowledge-base/ChunkDetailModal.tsx` — insert link after the meta row (line 74 in current file).
- `frontend/src/features/leaflet-generator/LeafletChunkDetailModal.tsx` — same change after meta row (line 66).
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs` — add `SourcePath` to the colocated `GetChunkDetailResponse` class.
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs` — add `SourcePath = chunk.Document.SourcePath,` to the response init (line 35 area).
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailRequest.cs` — add `SourcePath` to the colocated response class.
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs` — add `SourcePath = chunk.Document.SourcePath,` to the response init.

**Files NOT to modify:** Repositories (`KnowledgeBaseRepository.cs`, `LeafletRepository.cs`) — `.Include(c => c.Document)` is already in place. `IKnowledgeBaseRepository`, `ILeafletRepository` — unchanged.

### Interfaces and Contracts

**Backend DTO (additive, both classes):**
```csharp
public string? SourcePath { get; set; }
```

**Frontend helper (pure, no React):**
```ts
export function getSharePointLink(sourcePath: string | null | undefined): string | null {
  if (!sourcePath) return null;
  if (sourcePath.startsWith("https://")) return sourcePath;
  return null;
}
```

**Modal link block (identical in both modals):**
```tsx
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

**Sidebar consolidated section (replaces lines 140–149 and removes lines 299–314):**
```ts
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

### Data Flow

**Chunk detail click → SharePoint link rendering:**
```
User clicks chunk row in Documents tab
  → Modal opens, calls useChunkDetailQuery(chunkId)
  → API GET /api/knowledge-base/chunks/{id}
  → MediatR: GetChunkDetailRequest → GetChunkDetailHandler.Handle
  → repo.GetChunkByIdAsync (already includes Document)
  → handler maps chunk.Document.SourcePath → response.SourcePath
  → JSON body now contains "sourcePath": "https://..." or "upload/..." or ""
  → useChunkDetailQuery returns data with sourcePath populated
  → Modal calls getSharePointLink(data.sourcePath)
    → Returns the URL only if it starts with "https://"
  → Anchor renders or is omitted
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec drift (no duplicate id; no TrendingUp; no Campaigns) leads developer to look for code that doesn't exist and waste cycles. | MEDIUM | Amend the spec (see below) to describe the actual current structure. PR description should explicitly state "no duplicate id existed; consolidation is just a merge of `marketing` + `knowledgebase`". |
| `SourcePath` empty-string passed through could cause the helper to render an empty/broken link if logic is wrong. | LOW | The helper's `if (!sourcePath)` already covers `""`. Add explicit empty-string test case to `sharepointLink.test.ts`. |
| Removing the `Database` icon import breaks if used elsewhere in `Sidebar.tsx`. | LOW | Grep `Database` within `Sidebar.tsx` before removal; only drop if zero other usages. |
| Open-redirect via attacker-controlled `SourcePath` (e.g., `https://evil.com`). | LOW | `SourcePath` is server-controlled (set during ingestion from SharePoint Graph API or upload pipeline) and not user-mutable from the chunk detail surface. `rel="noopener noreferrer"` mitigates window.opener leakage. Acceptable for Phase 1. |
| OpenAPI client regen drift: stale generated TS file gets committed alongside hand edits. | LOW | The TS client is auto-regenerated on `npm run build`. Confirm post-build diff in `frontend/src/api/generated/api-client.ts` only contains the additive `sourcePath?: string` and commit it. |
| Orphaned `/knowledge-base/feedback` route confuses users who bookmarked it. | LOW | Out of scope; Phase 4 reconciles. Document the orphan in PR description. |
| Sidebar `expandedSections` state may show wrong group expanded if a user has the old `knowledgebase` id stored. | NONE | State is in-memory React `useState`; not persisted. No migration needed. |

## Specification Amendments

1. **FR-1 (rewrite premise):** Drop the language about a "duplicate `id: 'marketing'`" and `TrendingUp`-iconned orphan section. The actual change is: extend the existing single `marketing` section (currently containing only Kalendář + Generátor letáků), append Generátor článků, Poradenství (KB), and the conditional Feedback item, then remove the standalone `knowledgebase` section (lines 299–314 in current `Sidebar.tsx`). Remove `Database` from the lucide-react import. `TrendingUp` is already absent.

2. **FR-2 (delete entirely):** No `Campaigns` item exists. Nothing to decide.

3. **FR-4 / FR-5 file paths:** The response class lives in the same file as the request (`GetChunkDetailRequest.cs` and `GetLeafletChunkDetailRequest.cs`). Edit those files; do not create new `*Response.cs` files.

4. **FR-4 / FR-5 nullability note:** The domain entity's `SourcePath` is non-nullable `string` defaulting to empty. DTO field stays `string?` (defensive contract); handler assignment `SourcePath = chunk.Document.SourcePath` will produce `""` rather than `null` when no source is set. The frontend helper already treats `""` as "no link".

5. **FR-6 file location:** Move from `frontend/src/api/hooks/useSharePointLink.ts` to `frontend/src/utils/sharepointLink.ts`. The function is not a React hook and the directory `api/hooks/` is reserved for TanStack-Query wrappers. Update the import path in FR-7 and FR-8 accordingly.

6. **Implicit decision:** `KnowledgeBaseFeedbackPage` and the `/knowledge-base/feedback` route remain registered but are no longer linked from the sidebar. Phase 4 will reconcile. Document in PR.

## Prerequisites

None. All required infrastructure is in place:

- `Document.SourcePath` column already persisted (no migration).
- Both repositories already eager-load `Document` in `GetChunkByIdAsync`.
- All five referenced routes (`/marketing/calendar`, `/leaflet-generator`, `/articles`, `/knowledge-base`, plus the new `/marketing/feedback`) — four already exist; the fifth is added in this PR.
- Roles `knowledge_base_manager`, `leaflet_manager`, `article_generator` queryable via existing `hasRole` helper.
- `Megaphone` and `ExternalLink` already imported / available from `lucide-react`.
- OpenAPI generation already wired into `npm run build`.