# Unified Marketing Feedback Page — Design Spec

**Issue:** #934  
**Date:** 2026-05-06  
**Base branch:** `feature/genai_consistency`

---

## Goal

Replace the `/marketing/feedback` stub with a real page that aggregates feedback from all three GenAI features (KB, Leaflet, Article) in a tabbed layout. Each tab has its own stats bar, filter controls, paginated table, and row-detail modal.

---

## Structural Approach

**Option C — Direct replacement, no wrapper layer.**

Generic feedback components live in `frontend/src/components/feedback/`. The `KnowledgeBaseFeedbackPage` is updated to import them directly. The four old KB-specific component files (`FeedbackStatsBar`, `FeedbackFilters`, `FeedbackTable`, `FeedbackDetailModal` under `knowledge-base/`) are deleted after migration.

---

## Shared Types

**New file:** `frontend/src/components/feedback/types.ts`

```ts
export interface GenericFeedbackStats {
  totalItems: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

export interface FeedbackRow {
  id: string;
  primaryText: string;       // question / topic / title
  secondaryText?: string;    // answer excerpt / markdown excerpt / topic
  createdAt: string;
  userId?: string;
  precisionScore?: number | null;
  styleScore?: number | null;
  hasFeedback: boolean;
}

export interface FeedbackDetail extends FeedbackRow {
  feedbackComment?: string | null;
  extra?: React.ReactNode; // feature-specific fields rendered after scores
}

export interface GenericFeedbackParams {
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortDescending: boolean;
  hasFeedback?: boolean;
  userId?: string;
}
```

---

## Generic Components

All four live under `frontend/src/components/feedback/`.

### `GenericFeedbackStatsBar.tsx`

Props:
- `stats: GenericFeedbackStats | undefined`
- `isLoading: boolean`
- `itemLabel: string` — e.g. "dotazů", "generování", "článků"

Renders four cards: Total `[itemLabel]`, S feedbackem (with %), Ø Přesnost, Ø Styl. Shows skeleton divs when `isLoading`.

### `GenericFeedbackFilters.tsx`

Props:
- `hasFeedback: boolean | undefined`
- `sortBy: string`
- `sortDescending: boolean`
- `pageSize: number`
- `allowedSortColumns: { value: string; label: string }[]`
- Change handlers: `onHasFeedbackChange`, `onSortByChange`, `onSortDescendingChange`, `onPageSizeChange`

Renders four selects (Feedback filter, Sort by, Order, Page size). Sort column options are injected per feature.

### `GenericFeedbackTable.tsx`

Props:
- `rows: FeedbackRow[]`
- `isLoading: boolean`
- `totalCount: number`
- `pageNumber: number`
- `pageSize: number`
- `totalPages: number`
- `onPageChange: (page: number) => void`
- `onRowClick: (id: string) => void`
- `primaryLabel: string` — column header e.g. "Dotaz", "Téma"

Renders date, primary text (truncated), precision dots, style dots, feedback badge. Empty state: "Žádné záznamy nenalezeny." Pagination: «‹ page/total ›».

### `GenericFeedbackDetailModal.tsx`

Props:
- `detail: FeedbackDetail`
- `onClose: () => void`
- `primaryLabel: string`
- `secondaryLabel: string`

Renders date, userId (if present), primary text, secondary text, score dots (precision + style), feedback comment (if present). Closes on Escape key. Matches existing modal styling (fixed overlay, 75vw, shadow-xl).

---

## Adapter Hooks

All three live under `frontend/src/components/feedback/adapters/`.

### `useKbFeedbackAdapter.ts`

Wraps `useKnowledgeBaseFeedbackListQuery`. Maps:
- `log.question` → `primaryText`
- `log.answer.slice(0, 120)` → `secondaryText`
- `log.userId` → `userId`
- `log.precisionScore`, `log.styleScore`, `log.feedbackComment`, `log.hasFeedback` → same fields
- Stats: `totalQuestions` → `totalItems`, rest pass through

Returns: `{ rows: FeedbackRow[], stats: GenericFeedbackStats | undefined, totalCount, totalPages, pageNumber, isLoading, isError }`

### `useLeafletFeedbackAdapter.ts`

Wraps `useLeafletFeedbackListQuery`. The existing query returns untyped `any` — add `LeafletFeedbackListResponse` interface to `useLeaflet.ts`:

```ts
export interface LeafletFeedbackSummary {
  id: string;
  topic: string;
  audience: string;
  length: string;
  finalMarkdown: string;
  kbSourceCount: number;
  leafletSourceCount: number;
  durationMs: number;
  createdAt: string;
  userId: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
}

export interface LeafletFeedbackListResponse {
  items: LeafletFeedbackSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  stats: {
    totalGenerations: number;
    totalWithFeedback: number;
    avgPrecisionScore: number | null;
    avgStyleScore: number | null;
  };
}
```

Update `useLeafletFeedbackListQuery` return type to `Promise<LeafletFeedbackListResponse>`.

Maps:
- `item.topic` → `primaryText`
- `item.finalMarkdown.slice(0, 120)` → `secondaryText`
- `item.createdAt` → `createdAt`
- Stats: `totalGenerations` → `totalItems`
- `totalPages` calculated as `Math.ceil(totalCount / pageSize)`

### `useArticleFeedbackAdapter.ts`

Wraps `useArticleFeedbackListQuery`. Maps:
- `article.title ?? article.topic` → `primaryText`
- `article.topic` → `secondaryText`
- `article.requestedBy` → `userId`
- `article.generatedAt ?? ''` → `createdAt`
- Stats: `totalArticles` → `totalItems`

Note: Article params use `page` (not `pageNumber`) and `descending` (not `sortDescending`). The adapter accepts a `GenericFeedbackParams` (defined in `types.ts`) and translates: `pageNumber → page`, `sortDescending → descending`, `userId → requestedBy`.

---

## `MarketingFeedbackPage.tsx`

Replaces the stub at `frontend/src/pages/MarketingFeedbackPage.tsx`.

```
type FeatureTab = 'kb' | 'leaflet' | 'article'
```

Each tab maintains isolated state:
- `pageNumber`, `pageSize`, `hasFeedback`, `sortBy`, `sortDescending`
- Managed with three separate `useState` objects initialized to defaults: `pageNumber: 1, pageSize: 20, sortBy: 'CreatedAt', sortDescending: true`

Tab switch resets `selectedRowId` to `null`.

Authorization guard at top of render:

```tsx
const hasKb = useKnowledgeBaseUploadPermission();
const hasLeaflet = useLeafletUploadPermission();
const hasArticle = useArticleGeneratorPermission();
if (!hasKb && !hasLeaflet && !hasArticle) {
  return <div className="p-6 text-sm text-gray-500">Přístup odepřen.</div>;
}
```

Tab bar: three `<button>` elements with `border-b-2` active indicator (blue-600 when active, transparent otherwise).

Sort column options per tab:
- KB: `[{ value: 'CreatedAt', label: 'Datum' }, { value: 'PrecisionScore', label: 'Přesnost' }, { value: 'StyleScore', label: 'Styl' }]`
- Leaflet: same three options
- Article: `[{ value: 'CreatedAt', label: 'Datum' }, { value: 'PrecisionScore', label: 'Přesnost' }, { value: 'StyleScore', label: 'Styl' }]`

---

## `KnowledgeBaseFeedbackPage` Migration

Update imports to use generics from `frontend/src/components/feedback/`. Adapt calls:
- `FeedbackStatsBar` → `GenericFeedbackStatsBar` with `itemLabel="dotazů"` and `isLoading={isLoading}`
- `FeedbackFilters` → `GenericFeedbackFilters` with KB sort columns and individual change handlers
- `FeedbackTable` → inline map `FeedbackLogSummary[]` → `FeedbackRow[]` inside the page component, then pass to `GenericFeedbackTable` (do not use the adapter hook here — it lives in `MarketingFeedbackPage`)
- `FeedbackDetailModal` → `GenericFeedbackDetailModal` with `primaryLabel="Dotaz"`, `secondaryLabel="Odpověď"`, and `extra` rendering KB-specific fields (TopK, source count, duration)

After migration, delete the four old files from `frontend/src/components/knowledge-base/`:
- `FeedbackStatsBar.tsx`
- `FeedbackFilters.tsx`
- `FeedbackTable.tsx`
- `FeedbackDetailModal.tsx`

---

## Route & Sidebar

No changes needed:
- Route `/marketing/feedback` already points to `MarketingFeedbackPage` (registered in Phase 1)
- Sidebar already conditionally shows Feedback link for any of the three roles (verified in Phase 1)

---

## Tests

### Unit — Adapters
- `useKbFeedbackAdapter`: maps `FeedbackLogSummary` array → `FeedbackRow[]`; maps `FeedbackStatsDto` → `GenericFeedbackStats`
- `useLeafletFeedbackAdapter`: maps `LeafletFeedbackSummary` array → `FeedbackRow[]`; calculates `totalPages`
- `useArticleFeedbackAdapter`: maps `ArticleFeedbackSummary` array → `FeedbackRow[]`; `requestedBy` becomes `userId`; `generatedAt` becomes `createdAt`

### Unit — Generic Components
- `GenericFeedbackStatsBar`: loading skeleton shown when `isLoading`; correct label in heading
- `GenericFeedbackTable`: renders rows; pagination buttons disabled at boundaries; row click fires callback
- `GenericFeedbackDetailModal`: renders primary/secondary labels; shows scores; closes on Escape

### Integration — `MarketingFeedbackPage`
- Three tab buttons render
- Switching tabs resets selected row
- Role-less user sees "Přístup odepřen."
- Clicking a row opens modal; clicking again or pressing Escape closes it

---

## Verification Checklist

1. `npm run build` + `npm run lint` — clean
2. KB tab: question logs with stats bar
3. Leaflet tab: generation logs with stats bar
4. Article tab: article logs with stats bar
5. Filter by "S feedbackem" → only rated rows
6. Row click → modal with full text and scores
7. Regular user → Feedback link hidden in sidebar; direct URL → "Přístup odepřen."
8. `/knowledge-base/feedback` still loads KB-only page
9. `dotnet build` — no backend changes, must be clean
