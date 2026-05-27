I have enough context. Writing the review.

# Architecture Review: Fix `useArticleFeedbackListQuery` Type/Field Mismatches

## Skip Design: true

This is a frontend TypeScript-only correctness fix. No new screens, visual components, or layout decisions are introduced.

## Architectural Fit Assessment

The fix aligns with the established frontend pattern in `useArticles.ts`: each query function performs an explicit field-by-field mapping after `await client.<endpoint>()`, projecting the generated NSwag DTO onto a hand-written frontend interface. `useGetArticleQuery` (lines 149–196) and `useListArticlesQuery` (lines 125–147) already follow this shape. The brief originally described the hook as still using raw `fetch`, but a parallel refactor (see `docs/superpowers/plans/2026-05-25-article-frontend-hooks-bypass-refactor.md`) has already migrated `useArticleFeedbackListQuery` to `client.articles_FeedbackList`. So the work remaining is **strictly the rename + consumer/test alignment**, not the raw-fetch removal anymore. The spec text in FR-1 (“awaits `response.json()` into an untyped intermediate value”, “No direct cast of the raw payload”) is stale relative to current code — it should be re-stated against the generated-client shape (`raw` is now the typed NSwag DTO returned by `articles_FeedbackList`).

The main integration points are:
- The generated client `client.articles_FeedbackList` (already in use, types match backend: `items`, `createdAt`, `hasComment`).
- The hand-written interfaces `ArticleFeedbackListResponse` and `ArticleFeedbackSummary` (still expose legacy frontend names: `articles`, `generatedAt`, `hasFeedback`, `feedbackComment`).
- The single consumer `useArticleFeedbackAdapter.ts`, which projects article fields onto the domain-neutral `FeedbackDetail` interface in `components/feedback/types.ts`.

## Proposed Architecture

### Component Overview

```
backend                 generated NSwag DTO         frontend hook contract        generic UI contract
─────────────────────   ─────────────────────       ──────────────────────────    ──────────────────────────
GetArticleFeedback      ArticleFeedbackSummaryDto   ArticleFeedbackSummary        FeedbackRow / FeedbackDetail
ListHandler                                         (renamed in this work)        (unchanged, domain-neutral)
                        items                  →   articles                  →   rows
                        createdAt              →   createdAt  (was generatedAt)→  createdAt
                        hasComment             →   hasComment (was hasFeedback)→  hasFeedback (domain-neutral)
                        (not emitted)              [feedbackComment removed]  →   feedbackComment = null
```

Two contract layers are intentional:
1. **`ArticleFeedbackSummary`** — mirrors the backend article feedback list shape on the frontend. **This is where the rename lands.**
2. **`FeedbackRow` / `FeedbackDetail`** in `components/feedback/types.ts` — a domain-neutral row contract shared by article, leaflet, and KB adapters. Its `hasFeedback` and `feedbackComment` fields are about “does this row carry feedback for the generic table to render”, not about article-specific naming. **These do NOT get renamed.** Renaming them would cascade through `useLeafletFeedbackAdapter`, `useKbFeedbackAdapter`, `GenericFeedbackTable`, `GenericFeedbackDetailModal`, and their tests for no semantic gain.

### Key Design Decisions

#### Decision 1: Rename article-specific names; keep generic feedback-row names

**Options considered:**
- (A) Rename `FeedbackRow.hasFeedback` → `hasComment` everywhere (deep rename across all feedback adapters).
- (B) Rename only `ArticleFeedbackSummary.hasFeedback` → `hasComment`, keep `FeedbackRow.hasFeedback` as the domain-neutral name; map between them in the adapter.

**Chosen approach:** (B).

**Rationale:** `FeedbackRow.hasFeedback` is a UI abstraction that means “the row has scored/commented feedback worth showing.” For leaflet rows, it’s computed from score presence (`useLeafletFeedbackAdapter.ts:22`); for KB, it’s a per-log flag. Only the article version happens to be sourced from a `hasComment` backend bit. Cascading the rename would conflate the article-specific semantics with the generic abstraction and trigger a much larger churn (4+ files, 3+ test files) for no correctness benefit. The spec (FR-3, FR-4) only requires the frontend article type to match the backend; the generic row contract is out of scope and should stay put.

#### Decision 2: Keep `feedbackComment: null` flowing into `FeedbackDetail`

**Options considered:**
- (A) Remove `feedbackComment` from `FeedbackDetail` since the article list never has it.
- (B) Keep `FeedbackDetail.feedbackComment?: string | null` as-is; the article adapter explicitly passes `null` (other adapters pass real values).

**Chosen approach:** (B).

**Rationale:** Leaflet and KB adapters carry real comment text on their list rows (`useLeafletFeedbackAdapter.ts:23`, `useKbFeedbackAdapter.ts:23`), and `GenericFeedbackDetailModal.tsx:83` renders it. Removing the field breaks those consumers. The article adapter already passes `null` explicitly; keep that. The only thing that goes away is `ArticleFeedbackSummary.feedbackComment` (the hand-written article type field), because the backend never populates it for the list endpoint.

#### Decision 3: Use NSwag DTO directly as the “raw” input, not `response.json()`

**Options considered:**
- (A) Follow the spec literally: switch back to raw `fetch` + `response.json()` for the mapping.
- (B) Treat the typed DTO returned by `articles_FeedbackList` as the “raw” input to mapping, identical pattern to `useGetArticleQuery`.

**Chosen approach:** (B).

**Rationale:** A parallel refactor already removed the raw `fetch` from this hook. Reverting that to satisfy spec wording would undo a deliberate architectural improvement. The intent of FR-1 — explicit field-by-field mapping, no untyped passthrough — is satisfied by mapping from the typed DTO. The spec should be amended (see Specification Amendments).

#### Decision 4: New unit tests target the hook itself, not just the adapter

**Options considered:**
- (A) Cover the mapping only through the existing `useArticleFeedbackAdapter.test.ts`.
- (B) Add a new `frontend/src/api/hooks/__tests__/useArticles.test.ts` that mocks `getAuthenticatedApiClient().articles_FeedbackList` and asserts the hook’s mapping directly. Keep the adapter tests too.

**Chosen approach:** (B).

**Rationale:** FR-5 calls out three mapping cases (populated, all-nullable, empty `items`) that are properties of the hook’s mapping function, not the adapter. The adapter tests can’t cover the “missing `items` yields `[]`” case because they mock the hook’s output directly. Co-locating hook tests under `frontend/src/api/hooks/__tests__/` follows the established convention (`useKnowledgeBase.test.ts`, `useJournal.test.ts`, etc.).

## Implementation Guidance

### Directory / Module Structure

Files to modify:
- `frontend/src/api/hooks/useArticles.ts` — rename fields on `ArticleFeedbackSummary`, drop `feedbackComment` from this interface, update the mapping inside `useArticleFeedbackListQuery`.
- `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts` — read `article.createdAt`/`article.hasComment`; pass `feedbackComment: null` into `FeedbackDetail` (or omit, since it’s optional on `FeedbackDetail`).
- `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts` — update mock fixtures (`generatedAt` → `createdAt`, `hasFeedback` → `hasComment`, drop `feedbackComment` from the mocked article shape); rename the “maps generatedAt to createdAt” / “uses empty string for createdAt when generatedAt is null” test descriptions to reflect the new source field.

Files to create:
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` (new) — hook-level mapping tests per FR-5. Mock `getAuthenticatedApiClient` to return a stub whose `articles_FeedbackList` returns crafted DTOs.

Files explicitly NOT touched:
- `frontend/src/components/feedback/types.ts` (generic `FeedbackRow`/`FeedbackDetail`).
- `frontend/src/components/feedback/GenericFeedbackTable.tsx`, `GenericFeedbackDetailModal.tsx`, `GenericFeedbackFilters.tsx`.
- `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts`, `useKbFeedbackAdapter.ts` and their tests.
- `frontend/src/features/articles/ArticleFeedbackSection.tsx` — reads `article.feedbackComment` from `ArticleDetail` (not from the list summary). Stays as-is.
- `frontend/src/api/hooks/useLeaflet.ts` — its own `feedbackComment`/`hasFeedback` belong to a different domain type.
- Any backend file.

### Interfaces and Contracts

```ts
// Post-change shape — exactly what FR-3 mandates.
export interface ArticleFeedbackSummary {
  id: string;
  topic: string;
  title: string | null;
  requestedBy: string;
  createdAt: string | null;        // was generatedAt
  precisionScore: number | null;
  styleScore: number | null;
  hasComment: boolean;             // was hasFeedback
  // feedbackComment removed
}

export interface ArticleFeedbackListResponse {
  articles: ArticleFeedbackSummary[];  // mapped from raw.items
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  stats: ArticleFeedbackStats;
}
```

Adapter projection rule (single source of truth, in `useArticleFeedbackAdapter.ts`):

```
article.id              → row.id
article.title ?? topic  → row.primaryText
article.topic           → row.secondaryText
article.createdAt ?? '' → row.createdAt
article.requestedBy     → row.userId
article.precisionScore  → row.precisionScore
article.styleScore      → row.styleScore
article.hasComment      → row.hasFeedback        (generic semantics)
null                    → row.feedbackComment    (list endpoint never sends it)
```

### Data Flow

1. Adapter calls `useArticleFeedbackListQuery(params)`.
2. Hook calls `client.articles_FeedbackList(...)` → typed NSwag DTO `data`.
3. Hook maps:
   - `articles = (data.items ?? []).map(mapSummary)` where `mapSummary` returns the new `ArticleFeedbackSummary` shape with `createdAt`/`hasComment` (no `feedbackComment`).
   - `totalCount`/`page`/`pageSize`/`totalPages` with numeric defaults.
   - `stats` with the same default object as today.
4. React Query caches the typed `ArticleFeedbackListResponse`.
5. Adapter reads `query.data.articles` and projects each row onto `FeedbackDetail`, mapping `hasComment` → `FeedbackRow.hasFeedback` and `createdAt` → `FeedbackRow.createdAt`.
6. `GenericFeedbackTable` and `GenericFeedbackDetailModal` consume `FeedbackRow.hasFeedback` / `FeedbackDetail.feedbackComment` without any change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Cascading the rename into the generic `FeedbackRow.hasFeedback` would touch leaflet + KB adapters and their tests with no semantic benefit. | Medium | Decision 1 above — restrict rename to `ArticleFeedbackSummary`. Reviewer should reject any PR that also renames `FeedbackRow.hasFeedback`. |
| Existing adapter tests use the legacy article field names in their mocks, so they currently pass against the **wrong** runtime shape. Renaming only the interface without updating mocks could leave tests green while the type is wrong. | High | Update `useArticleFeedbackAdapter.test.ts` mock fixtures (`mockArticle`, `mockArticleNoTitle`) in the same PR. Adapter test names that mention `generatedAt` must be renamed too. |
| `ArticleFeedbackSection.tsx` reads `article.feedbackComment` from `ArticleDetail` (the **detail** type, not the list summary) — easy to conflate and accidentally edit. | Low | Restrict edits to `ArticleFeedbackSummary`/`ArticleFeedbackListResponse`. Leave `ArticleDetail.feedbackComment` and `ArticleFeedbackSection.tsx` alone. |
| The spec wording assumes raw `fetch` + `response.json()`, but the hook already uses the generated client. Following the spec literally would regress that. | High | See Specification Amendments below — rewrite FR-1’s wording to map from the typed DTO. |
| Stats default-object branch is duplicated (lines 287–298). Spec NFR-4 forbids new abstractions just for the fix. | Low | Leave the duplication intact; the fix scope is rename-only. |
| Removing `feedbackComment` from `ArticleFeedbackSummary` could regress any consumer that today happens to read `article.feedbackComment` (even though it’s always `null`). | Low | Grep confirms only the adapter reads it on a list row (`useArticleFeedbackAdapter.ts:24`), and that line will be updated. `ArticleFeedbackSection` reads from `ArticleDetail`, a different type. |
| A future addition of `feedbackComment` to the list endpoint would re-introduce the type. | Low | Documented as out-of-scope; if/when added on the backend, regenerate the client and extend `ArticleFeedbackSummary` then. |

## Specification Amendments

1. **FR-1 wording is stale.** Replace:
   > “The query function awaits `response.json()` into an untyped intermediate value.”
   with:
   > “The query function calls `client.articles_FeedbackList(...)` and treats the returned NSwag DTO as the input to a field-by-field mapping. No untyped passthrough or direct cast of the DTO to `ArticleFeedbackListResponse` remains; every field on the returned `ArticleFeedbackListResponse` is populated by explicit mapping.”
   This preserves the intent (no implicit passthrough) without forcing a regression to raw `fetch`.

2. **FR-3 “field types unchanged” clarification.** The current `ArticleFeedbackSummary.id` is typed `string` in the hand-written interface, but the generated DTO exposes `id` as `string | undefined` and the existing mapping uses `item.id ?? ''`. Keep the post-change interface as `id: string` and continue defaulting to `''`. State this explicitly in FR-3 so reviewers do not flag it.

3. **FR-4 scope clarification.** Add a sentence: “`FeedbackRow`/`FeedbackDetail` in `components/feedback/types.ts` are domain-neutral and are NOT renamed. Article-specific renames stop at `ArticleFeedbackSummary`; the article adapter performs the projection from `hasComment` to `FeedbackRow.hasFeedback`.”

4. **FR-5 test placement.** Add: “Hook-level mapping tests live in `frontend/src/api/hooks/__tests__/useArticles.test.ts` (new file, follows existing convention). Adapter-level tests (`useArticleFeedbackAdapter.test.ts`) get their mock fixtures updated in the same PR but remain focused on adapter projection, not on the hook’s mapping.”

5. **Out-of-scope addendum.** Explicitly add: “Renaming `FeedbackRow.hasFeedback` or `FeedbackDetail.feedbackComment` is out of scope. The leaflet/KB adapters and the generic feedback table/modal remain untouched.”

## Prerequisites

- None. No backend change, no migration, no config flag, no infrastructure work. The NSwag client already exposes `articles_FeedbackList` with the correct backend field names; no client regeneration is required for this fix. The PR is a single-commit, frontend-only TypeScript refactor with accompanying test updates and one new test file.