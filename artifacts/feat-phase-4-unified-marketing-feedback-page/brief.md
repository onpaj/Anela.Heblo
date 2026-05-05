# Phase 4 — Unified Marketing Feedback Page

## Goal
Replace the KB-only `/knowledge-base/feedback` admin page with a single `/marketing/feedback` page that shows feedback across all three features (KB, Leaflet, Article) in tabs, with per-feature stats bars and filterable/paginated tables.

## Dependency
Phases 1, 2, and 3 must be complete — all three features need their feedback endpoints before this page can aggregate them.

---

## Step 1 — Generalize existing KB feedback components

The four KB-specific feedback components live in `frontend/src/components/knowledge-base/`:
- `FeedbackStatsBar.tsx`
- `FeedbackFilters.tsx`
- `FeedbackTable.tsx`
- `FeedbackDetailModal.tsx`

They currently hardcode KB-specific field names (`question`, `answer`, `sourceCount`, etc.).

### Strategy
Create generic versions under `frontend/src/components/feedback/` that accept typed props. The KB-specific components become thin wrappers that pass KB types to the generics.

---

### 1a. `GenericFeedbackStatsBar.tsx`

**New file**: `frontend/src/components/feedback/GenericFeedbackStatsBar.tsx`

```tsx
interface GenericFeedbackStats {
  totalItems: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

interface GenericFeedbackStatsBarProps {
  stats: GenericFeedbackStats | undefined;
  isLoading: boolean;
  itemLabel: string;  // "pytaní", "generování letáku", "článků"
}

export default function GenericFeedbackStatsBar({ stats, isLoading, itemLabel }: GenericFeedbackStatsBarProps) {
  // Render four stat cards: Total [itemLabel], S feedbackem, Ø Přesnost, Ø Styl
  // Skeleton loading state when isLoading
}
```

Update `frontend/src/components/knowledge-base/FeedbackStatsBar.tsx` to import and delegate to `GenericFeedbackStatsBar` with `itemLabel="dotazů"`.

---

### 1b. `GenericFeedbackFilters.tsx`

**New file**: `frontend/src/components/feedback/GenericFeedbackFilters.tsx`

```tsx
interface GenericFeedbackFiltersProps {
  hasFeedback: boolean | undefined;
  userId: string | undefined;
  sortBy: string;
  sortDescending: boolean;
  pageSize: number;
  onHasFeedbackChange: (v: boolean | undefined) => void;
  onUserIdChange: (v: string | undefined) => void;
  onSortByChange: (v: string) => void;
  onSortDescendingChange: (v: boolean) => void;
  onPageSizeChange: (v: number) => void;
  allowedSortColumns: { value: string; label: string }[];
}
```

Update `FeedbackFilters.tsx` (KB) to wrap `GenericFeedbackFilters` with KB-specific sort column options.

---

### 1c. `GenericFeedbackTable.tsx`

**New file**: `frontend/src/components/feedback/GenericFeedbackTable.tsx`

Define a `FeedbackRow` interface generic enough for all three features:

```tsx
interface FeedbackRow {
  id: string;
  primaryText: string;    // question / topic / title
  secondaryText?: string; // answer (first 120 chars) / final markdown (first 120 chars) / html snippet
  createdAt: string;
  userId?: string;
  precisionScore?: number;
  styleScore?: number;
  hasFeedback: boolean;
}

interface GenericFeedbackTableProps {
  rows: FeedbackRow[];
  isLoading: boolean;
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onRowClick: (id: string) => void;
  primaryLabel: string;  // "Dotaz" / "Téma" / "Téma článku"
}
```

The existing `FeedbackTable.tsx` (KB) adapts its data to `FeedbackRow[]` and delegates to `GenericFeedbackTable`.

---

### 1d. `GenericFeedbackDetailModal.tsx`

**New file**: `frontend/src/components/feedback/GenericFeedbackDetailModal.tsx`

```tsx
interface FeedbackDetail {
  id: string;
  primaryText: string;
  secondaryText: string;
  createdAt: string;
  userId?: string;
  precisionScore?: number;
  styleScore?: number;
  feedbackComment?: string;
  // feature-specific extra fields passed as slot
  extra?: React.ReactNode;
}

interface GenericFeedbackDetailModalProps {
  detail: FeedbackDetail;
  onClose: () => void;
  primaryLabel: string;
  secondaryLabel: string;
}
```

The existing `FeedbackDetailModal.tsx` (KB) adapts its `FeedbackLogSummary` to `FeedbackDetail` and delegates.

---

## Step 2 — Feature-specific feedback list adapters

Create three small adapter hooks/components that normalize each feature's API response into the generic types.

### 2a. KB adapter

**New file**: `frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts`

```ts
import { useKnowledgeBaseFeedbackListQuery } from '../../../api/hooks/useKnowledgeBase';

export function useKbFeedbackRows(params: KbFeedbackParams) {
  const query = useKnowledgeBaseFeedbackListQuery(params);

  const rows = query.data?.logs.map((log): FeedbackRow => ({
    id: log.id,
    primaryText: log.question,
    secondaryText: log.answer.slice(0, 120),
    createdAt: log.createdAt,
    userId: log.userId,
    precisionScore: log.precisionScore,
    styleScore: log.styleScore,
    hasFeedback: log.precisionScore != null || log.styleScore != null,
  })) ?? [];

  const stats: GenericFeedbackStats | undefined = query.data?.stats
    ? {
        totalItems: query.data.stats.totalQuestions,
        totalWithFeedback: query.data.stats.totalWithFeedback,
        avgPrecisionScore: query.data.stats.avgPrecisionScore,
        avgStyleScore: query.data.stats.avgStyleScore,
      }
    : undefined;

  return { rows, stats, totalCount: query.data?.totalCount ?? 0, isLoading: query.isLoading };
}
```

### 2b. Leaflet adapter

**New file**: `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts`

```ts
export function useLeafletFeedbackRows(params) {
  const query = useLeafletFeedbackListQuery(params);
  const rows = query.data?.logs.map((log): FeedbackRow => ({
    id: log.id,
    primaryText: log.topic,
    secondaryText: log.finalMarkdown.slice(0, 120),
    createdAt: log.createdAt,
    userId: log.userId,
    precisionScore: log.precisionScore,
    styleScore: log.styleScore,
    hasFeedback: log.precisionScore != null || log.styleScore != null,
  })) ?? [];
  // ... same stats mapping ...
}
```

### 2c. Article adapter

**New file**: `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts`

```ts
export function useArticleFeedbackRows(params) {
  const query = useArticleFeedbackListQuery(params);
  const rows = query.data?.articles.map((a): FeedbackRow => ({
    id: a.id,
    primaryText: a.title ?? a.topic,
    secondaryText: a.topic,
    createdAt: a.createdAt,
    userId: a.requestedBy,
    precisionScore: a.precisionScore,
    styleScore: a.styleScore,
    hasFeedback: a.precisionScore != null || a.styleScore != null,
  })) ?? [];
  // ... stats mapping ...
}
```

---

## Step 3 — `MarketingFeedbackPage.tsx`

**File**: `frontend/src/pages/MarketingFeedbackPage.tsx` (replace the stub created in Phase 1)

```tsx
type FeatureTab = 'kb' | 'leaflet' | 'article';

const TAB_LABELS: Record<FeatureTab, string> = {
  kb: 'Poradenství (KB)',
  leaflet: 'Letáky',
  article: 'Články',
};

export default function MarketingFeedbackPage() {
  const [activeTab, setActiveTab] = useState<FeatureTab>('kb');
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);

  // Each tab has its own filter state (page, hasFeedback, userId, sort)
  const [kbParams, setKbParams] = useReducer(..., defaultFeedbackParams);
  const [leafletParams, setLeafletParams] = useReducer(..., defaultFeedbackParams);
  const [articleParams, setArticleParams] = useReducer(..., defaultFeedbackParams);

  const { rows: kbRows, stats: kbStats, totalCount: kbTotal, isLoading: kbLoading } =
    useKbFeedbackRows(kbParams);
  const { rows: leafletRows, stats: leafletStats, totalCount: leafletTotal, isLoading: leafletLoading } =
    useLeafletFeedbackRows(leafletParams);
  const { rows: articleRows, stats: articleStats, totalCount: articleTotal, isLoading: articleLoading } =
    useArticleFeedbackRows(articleParams);

  const activeRows = { kb: kbRows, leaflet: leafletRows, article: articleRows }[activeTab];
  const activeStats = { kb: kbStats, leaflet: leafletStats, article: articleStats }[activeTab];
  const activeTotal = { kb: kbTotal, leaflet: leafletTotal, article: articleTotal }[activeTab];
  const activeLoading = { kb: kbLoading, leaflet: leafletLoading, article: articleLoading }[activeTab];

  const selectedRow = activeRows.find(r => r.id === selectedRowId) ?? null;

  return (
    <div className="p-6 max-w-6xl mx-auto space-y-6">
      <h1 className="text-xl font-semibold text-gray-900">Feedback</h1>

      {/* Tab bar */}
      <div className="flex gap-1 border-b border-gray-200">
        {(Object.keys(TAB_LABELS) as FeatureTab[]).map(tab => (
          <button
            key={tab}
            onClick={() => { setActiveTab(tab); setSelectedRowId(null); }}
            className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
              activeTab === tab
                ? 'border-blue-600 text-blue-700'
                : 'border-transparent text-gray-600 hover:text-gray-900'
            }`}
          >
            {TAB_LABELS[tab]}
          </button>
        ))}
      </div>

      <GenericFeedbackStatsBar
        stats={activeStats}
        isLoading={activeLoading}
        itemLabel={activeTab === 'kb' ? 'dotazů' : activeTab === 'leaflet' ? 'generování' : 'článků'}
      />

      <GenericFeedbackFilters
        {...activeTabParams}
        allowedSortColumns={SORT_COLUMNS}
        onHasFeedbackChange={...}
        // ... dispatch to the active tab's params reducer
      />

      <GenericFeedbackTable
        rows={activeRows}
        isLoading={activeLoading}
        totalCount={activeTotal}
        pageNumber={activeTabParams.pageNumber}
        pageSize={activeTabParams.pageSize}
        onPageChange={...}
        onRowClick={setSelectedRowId}
        primaryLabel={activeTab === 'kb' ? 'Dotaz' : 'Téma'}
      />

      {selectedRow && (
        <GenericFeedbackDetailModal
          detail={rowToDetail(selectedRow)}
          onClose={() => setSelectedRowId(null)}
          primaryLabel={activeTab === 'kb' ? 'Dotaz' : 'Téma'}
          secondaryLabel={activeTab === 'kb' ? 'Odpověď' : 'Výstup'}
        />
      )}
    </div>
  );
}
```

---

## Step 4 — Keep old `/knowledge-base/feedback` page

**Decision**: keep `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx` and its route (`/knowledge-base/feedback`). The sidebar link under `knowledgebase` no longer exists (moved to Marketing), but the URL still works as a deep link. This avoids breaking any bookmarks and is a one-line non-change.

If the old page is confusing long-term, it can be removed after Phase 4 ships and has been validated.

---

## Step 5 — Authorization check on `MarketingFeedbackPage`

The page should only render for users with at least one manager role. Add a guard in the page:

```tsx
const { hasKbManagerRole } = useKnowledgeBaseUploadPermission();
const { hasLeafletManagerRole } = useLeafletUploadPermission();
const { hasArticleGeneratorRole } = useArticleGeneratorPermission();

const canView = hasKbManagerRole || hasLeafletManagerRole || hasArticleGeneratorRole;

if (!canView) {
  return <div className="p-6 text-sm text-gray-500">Přístup odepřen.</div>;
}
```

The sidebar link already conditionally shows based on any manager role (Phase 1 step 1b). This is defense-in-depth.

---

## Step 6 — Sidebar: update feedback link role check

**File**: `frontend/src/components/layout/Sidebar.tsx`

Update the Feedback sidebar item condition from Phase 1 to use the same three-role OR:

```tsx
...(hasRole("knowledge_base_manager") || hasRole("leaflet_manager") || hasRole("article_generator")
  ? [{ id: "marketing-feedback", name: "Feedback", href: "/marketing/feedback" }]
  : []),
```

(This was already written this way in Phase 1 — no change needed if Phase 1 was implemented correctly.)

---

## Step 7 — Route registration

**File**: `frontend/src/App.tsx`

The route was registered as a stub in Phase 1. The import now points to the real page — no change to `App.tsx` needed (same import, same path, page content replaced).

---

## Tests to write

### Frontend
- `MarketingFeedbackPage.test.tsx`:
  - Renders three tab buttons.
  - Switching tabs shows correct stats bar label and resets selected row.
  - Role-less user sees "Přístup odepřen."
  - Clicking a row opens the detail modal.
- `GenericFeedbackStatsBar.test.tsx` — loading skeleton, populated stats.
- `GenericFeedbackTable.test.tsx` — renders rows, paging buttons, row click fires callback.
- `GenericFeedbackDetailModal.test.tsx` — renders primary/secondary text and score fields.
- Adapter unit tests: each adapter maps the raw API response correctly to `FeedbackRow[]` and `GenericFeedbackStats`.

---

## Verification

1. `npm run build` + `npm run lint` — clean.
2. Log in as `knowledge_base_manager` → Marketing → Feedback → page loads with three tabs.
3. Poradenství (KB) tab shows KB question logs with stats bar.
4. Switch to Letáky tab → Leaflet generations shown (requires Phase 2 data in DB).
5. Switch to Články tab → Articles with feedback shown (requires Phase 3 data in DB).
6. Filter by "S feedbackem" → only rated rows shown.
7. Click row → detail modal shows full primary text, secondary text, scores.
8. Log in as regular user → Marketing → Feedback link absent from sidebar.
9. Navigate directly to `/marketing/feedback` as regular user → "Přístup odepřen."
10. Old `/knowledge-base/feedback` URL still works (shows KB-only feedback page).
11. `dotnet build` — no backend changes in this phase; build must still be clean.

---

## Cleanup (optional, after validation)

- Remove old `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx` and its route once the unified page is confirmed in production.
- Remove the sidebar `kb-feedback` link reference (already removed in Phase 1, just confirm it's gone).
- Remove the KB-specific `FeedbackStatsBar.tsx`, `FeedbackFilters.tsx`, `FeedbackTable.tsx`, `FeedbackDetailModal.tsx` once the generics are confirmed to be the only consumers.
