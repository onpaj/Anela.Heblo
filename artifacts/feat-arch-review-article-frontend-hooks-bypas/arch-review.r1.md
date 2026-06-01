# Architecture Review: Refactor Article Frontend Hooks to Use Generated NSwag Client Methods

## Skip Design: true

No UI components, screens, or visual design changes. The refactor is purely internal to three React Query `queryFn` bodies; consumer components (`ArticleDebugPanel.tsx`, `useArticleFeedbackAdapter.ts`, `ArticleDetail.tsx`) are untouched by design.

## Architectural Fit Assessment

The proposed refactor **strengthens** existing architecture rather than introducing new patterns. The codebase already establishes a canonical shape for Article hooks:

1. Acquire the typed client via `getAuthenticatedApiClient()` (`frontend/src/api/client.ts:232`).
2. Call a generated method (e.g. `articles_GetById`, `articles_List`, `articles_Generate`).
3. Project the generated DTO into a hook-local consumer-facing interface with explicit per-field mapping, coalescing `undefined` to safe defaults and converting `Date → string` via `toISOString()`.

`useListArticlesQuery` (`useArticles.ts:125-147`) and the non-cast portion of `useGetArticleQuery` (`useArticles.ts:149-198`) follow this pattern verbatim. The three hooks named in the spec are the **only** Article hooks that deviate. The refactor brings them into conformance — no new module, no new abstraction, no new dependency.

The deliberate exemption of `useSubmitArticleFeedbackMutation` is well-justified: 409 is a non-exceptional branch and `articles_FeedbackSubmit` would throw `ApiException` on non-2xx, which would force `useMutation` consumers to inspect thrown errors instead of return values. Leaving it as raw fetch with a dated `// TODO` is acceptable for a solo-dev workspace (CLAUDE.md confirms "Solo developer + AI-assisted PR review"); a separate GitHub issue would add ceremony without adding traceability.

The integration boundary that matters: the **DTO ⇄ local interface** boundary. The generated client is regenerated on every backend OpenAPI change; the local hook-output interfaces are stable consumer contracts. Mapping at this boundary is the architectural seam — and the spec correctly preserves it.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  Consumer Components                                                │
│  - ArticleDebugPanel  - ArticleDetail  - useArticleFeedbackAdapter  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ reads: ArticleTrace, ArticleDetail,
                               │        ArticleFeedbackListResponse
                               │ (stable LOCAL interfaces)
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│  React Query Hooks  (frontend/src/api/hooks/)                       │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ useArticleTraceQuery         (useArticleTrace.ts)             │  │
│  │ useGetArticleQuery           (useArticles.ts)                 │  │
│  │ useArticleFeedbackListQuery  (useArticles.ts)                 │  │
│  │                                                               │  │
│  │  queryFn:                                                     │  │
│  │    1. getAuthenticatedApiClient() ───────► typed ApiClient    │  │
│  │    2. client.articles_X(...)      ───────► generated DTO      │  │
│  │    3. explicit per-field mapping  ───────► local interface    │  │
│  └───────────────────────────────────────────────────────────────┘  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ HTTPS (Authorization header injected
                               │        by authenticated http.fetch)
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Generated NSwag Client  (frontend/src/api/generated/api-client.ts) │
│  - ApiClient.articles_GetTrace(id)                                  │
│  - ApiClient.articles_FeedbackList(...)                             │
│  - ApiClient.articles_GetById(id)                                   │
│  - DTOs: GetArticleTraceResponse, GetArticleFeedbackListResponse,   │
│          GetArticleResponse, ArticleGenerationStepDto, ...          │
└─────────────────────────────────────────────────────────────────────┘
```

The dashed boundary between hook layer and generated-client layer is the contract the spec defends: generated DTOs never leak past the hook; consumer code only ever sees the local interfaces.

### Key Design Decisions

#### Decision 1: Explicit inline mapping, no shared mapper utility

**Options considered:**
- (a) Inline per-field projection inside each `queryFn` (matches existing `useGetArticleQuery` style).
- (b) Extract `mapTraceDto(...)`, `mapFeedbackListDto(...)` into a sibling `articleMappers.ts` module.
- (c) Generate runtime validators (e.g. Zod) from the local interfaces and parse generated DTOs through them.

**Chosen approach:** (a) — inline per-field projection inside each `queryFn`.

**Rationale:** Three mappings, each used in exactly one hook. Extraction (b) creates a separate file with no second caller, violating YAGNI and breaking from the established convention in `useListArticlesQuery` and `useGetArticleQuery` (which inline their mappings). Approach (c) adds a runtime parsing dependency for a problem that does not exist — the generated client's `fromJS` already constructs typed instances. Inline mapping keeps the data shape and the only call site co-located; future extraction is cheap if a second caller emerges.

#### Decision 2: Project missing `stats`/optional pagination fields to safe defaults rather than mutate local interfaces

**Options considered:**
- (a) Coalesce optional generated fields (`stats?`, `totalPages?`, `totalCount?`) to safe defaults inside the projection, keeping local interface fields required.
- (b) Loosen the local `ArticleFeedbackListResponse` interface to make `stats`, `totalPages`, etc. optional, matching the generated DTO.

**Chosen approach:** (a) — coalesce in the projection.

**Rationale:** The local interface is a **consumer contract**. `useArticleFeedbackAdapter.ts` reads `query.data.stats` and `query.data.totalPages` directly. Weakening the interface (b) would force every consumer to add `?.` chains it doesn't need today — scope creep into the adapter and any future consumer. Coalescing in the projection preserves both the contract and the static guarantee. Default for `stats` when absent: `{ totalArticles: 0, totalWithFeedback: 0, avgPrecisionScore: null, avgStyleScore: null }`. In practice the backend always returns `stats` (verified in `GetArticleFeedbackListHandler.cs:58-64`), so the default is defensive only.

#### Decision 3: `feedbackComment: null` per-item projection is behavior-preserving, not a regression

**Options considered:**
- (a) Project `feedbackComment: null` for every list item (per spec).
- (b) Regenerate NSwag client after extending backend `ArticleFeedbackSummary` to include `FeedbackComment` so the field survives generation.
- (c) Keep raw fetch indefinitely for this hook to preserve "whatever the backend sends."

**Chosen approach:** (a).

**Rationale:** Confirmed by reading `backend/.../GetArticleFeedbackListHandler.cs:42-54`: the backend response **never includes** `FeedbackComment` per item — only the boolean `HasComment`. The current raw-fetch implementation returns `response.json()` unchanged, so today's value at the consumer is **also always `null`/`undefined`**. Projecting to `null` is exact behavior preservation, not data loss. (b) is correct long-term hygiene but expands scope into backend + client regeneration. (c) defeats the refactor.

#### Decision 4: Keep `useSubmitArticleFeedbackMutation` outside the refactor; flag with a single dated TODO

**Options considered:**
- (a) TODO comment only (per spec FR-5).
- (b) Refactor to `articles_FeedbackSubmit` and translate `ApiException` with `status === 409` into the `{ alreadySubmitted: true }` return.
- (c) Open a separate GitHub issue.

**Chosen approach:** (a).

**Rationale:** The 409 branch is a deliberate non-error business outcome that `useMutation` consumers read off the return value, not the thrown error. The generated client throws `ApiException` on every non-2xx, which would require catch-and-rethrow logic inside the `mutationFn` — net code growth, not reduction. (c) is bureaucratic for a solo-dev workspace per CLAUDE.md. A single dated TODO at the fragile line is the right cost/visibility trade-off for this PR.

## Implementation Guidance

### Directory / Module Structure

**No new files, no new directories.** All edits land in two existing files:

```
frontend/src/api/hooks/
├── useArticleTrace.ts     # modify queryFn body in useArticleTraceQuery
└── useArticles.ts          # modify queryFn body in useArticleFeedbackListQuery,
                            #         remove `as any` casts in useGetArticleQuery,
                            #         add one-line TODO above useSubmitArticleFeedbackMutation's fetch
```

No file rename, no export-shape change, no new dependency.

### Interfaces and Contracts

**Stable consumer contracts (do not touch):**

```typescript
// useArticleTrace.ts — line 4–21 (unchanged)
export interface ArticleGenerationStep { /* 11 required fields */ }
export interface ArticleTrace { articleId: string; steps: ArticleGenerationStep[]; }

// useArticles.ts — line 78–104 (unchanged)
export interface ArticleFeedbackSummary { /* 8 fields, feedbackComment: string | null */ }
export interface ArticleFeedbackStats { /* 4 fields */ }
export interface ArticleFeedbackListResponse {
  articles: ArticleFeedbackSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  stats: ArticleFeedbackStats;   // REQUIRED — must project a default if generated DTO omits
}
```

**Hook signatures (must match current, not the spec's "signature" example):**

```typescript
// ACTUAL current signature — preserve as-is
export const useArticleTraceQuery = (id: string, enabled: boolean) => UseQueryResult<ArticleTrace, Error>;

// (the spec writes this as `(id, options?: { enabled?: boolean })`; that is wrong — see Spec Amendment #1)
```

**Generated inputs (read-only):**

```typescript
client.articles_GetTrace(id: string)
  → Promise<GetArticleTraceResponse>           // articleId?, steps?: ArticleGenerationStepDto[]

client.articles_FeedbackList(
  hasFeedback:    boolean | null | undefined,
  requestedBy:    string  | null | undefined,
  sortBy:         string  | undefined,
  sortDescending: boolean | undefined,         // NOTE: parameter is named `sortDescending` in the generated client
  page:           number  | undefined,
  pageSize:       number  | undefined,
) → Promise<GetArticleFeedbackListResponse>

client.articles_GetById(id: string) → Promise<GetArticleResponse>  // already used; just drop the `as any` casts
```

### Data Flow

For `useArticleTraceQuery`:

```
component (ArticleDebugPanel) 
  → useArticleTraceQuery(id, enabled)
    → getAuthenticatedApiClient()         [client.ts:232 — injects Authorization]
    → client.articles_GetTrace(id)         [generated, /api/articles/{id}/trace, GET]
    → GetArticleTraceResponse              [Dates parsed; steps?: ArticleGenerationStepDto[]]
    → INLINE PROJECTION                    [per spec FR-1: 11 fields, dates → ISO strings]
    → ArticleTrace                         [local, all step fields required]
  → React Query cache (key: ['articles','trace', id])
  → render
```

For `useArticleFeedbackListQuery`:

```
adapter (useArticleFeedbackAdapter)
  → useArticleFeedbackListQuery({ page, pageSize, sortBy, descending, hasFeedback, requestedBy })
    → getAuthenticatedApiClient()
    → client.articles_FeedbackList(hasFeedback ?? null,
                                   requestedBy ?? null,
                                   sortBy,
                                   descending,
                                   page,
                                   pageSize)
    → GetArticleFeedbackListResponse        [items: ArticleFeedbackSummary (generated, no feedbackComment),
                                             stats?, page?, pageSize?, totalCount?, totalPages?]
    → INLINE PROJECTION:
        articles: items.map(item => ({
          id, topic, title,
          requestedBy: item.requestedBy ?? '',
          generatedAt: item.createdAt?.toISOString() ?? null,
          precisionScore: item.precisionScore ?? null,
          styleScore: item.styleScore ?? null,
          hasFeedback: item.hasComment ?? false,
          feedbackComment: null,              // confirmed: backend never emits this field for list items
        })),
        totalCount: data.totalCount ?? 0,
        page: data.page ?? params.page ?? 1,
        pageSize: data.pageSize ?? params.pageSize ?? 20,
        totalPages: data.totalPages ?? 0,
        stats: data.stats
          ? { totalArticles: data.stats.totalArticles ?? 0,
              totalWithFeedback: data.stats.totalWithFeedback ?? 0,
              avgPrecisionScore: data.stats.avgPrecisionScore ?? null,
              avgStyleScore: data.stats.avgStyleScore ?? null }
          : { totalArticles: 0, totalWithFeedback: 0,
              avgPrecisionScore: null, avgStyleScore: null },
    → React Query cache (key: ['articles','feedback-list', params])
    → adapter projects to FeedbackDetail[] + GenericFeedbackStats
```

For `useGetArticleQuery`:

```
[unchanged flow, just remove three `as any` casts on response.{precisionScore, styleScore, feedbackComment}]
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec's documented `useArticleTraceQuery` signature `(id, options?)` does not match the actual `(id, enabled)` shape in code. Following the spec literally would break `ArticleDebugPanel`. | HIGH | Implementer must read the actual signature first and preserve it. See Spec Amendment #1. |
| Backend response for feedback list could in future legitimately include `feedbackComment` per item; generated client would silently discard it because `ArticleFeedbackSummary.fromJS` doesn't read it. Refactor entrenches this. | LOW | Document in code comment near the `feedbackComment: null` projection. If/when the field becomes meaningful, both backend DTO and NSwag regeneration must follow — same workflow as any other API contract change. |
| `useSubmitArticleFeedbackMutation`'s residual `(apiClient as any).baseUrl` access carries the same fragility this PR removes elsewhere. | MEDIUM | Single dated `// TODO(arch-review 2026-05-25): ...` comment per spec FR-5. Acceptable for solo-dev workflow. |
| Generated `articles_FeedbackList` parameter name is `sortDescending`, not `descending`. Positional invocation is correct, but a reader scanning local code may be confused. | LOW | Match by position, not by name. No code change required; mention in PR description. |
| Local `ArticleFeedbackListResponse.stats` is **required** but generated DTO marks `stats?` optional. Naïve `data.stats` forwarding produces `stats: undefined`, which breaks the consumer contract under strict TypeScript. | MEDIUM | Project a safe default object when `data.stats` is undefined (see Data Flow). The backend always returns stats today, so the default is defensive. |
| Removing `(response as any)` casts in `useGetArticleQuery` could surface type errors if the generated client falls behind the backend in a future regeneration. | LOW | The fields are present in the current generated file (`api-client.ts:12879-12881`); regenerate before merging if the backend OpenAPI moves. TypeScript will catch the drift loudly at build time — exactly the property we want. |

## Specification Amendments

### Amendment #1 — Correct the documented `useArticleTraceQuery` signature
The "API / Interface Design" section of spec.r2.md (under "Internal hook signatures — unchanged") writes:

```typescript
export function useArticleTraceQuery(id: string, options?: { enabled?: boolean }): ...
```

The **actual** current signature (`useArticleTrace.ts:23`) is:

```typescript
export const useArticleTraceQuery = (id: string, enabled: boolean) => ...
```

`ArticleDebugPanel.tsx:74` calls it as `useArticleTraceQuery(articleId, expanded)`. The refactor MUST preserve the actual signature; the spec's example shape is wrong and must not be followed.

### Amendment #2 — Add per-item field set for `ArticleFeedbackSummary` projection
Spec FR-2 says "every field the consumer reads must be present with its current name and type" but only enumerates a subset. The implementer must include all 8 fields of the local `ArticleFeedbackSummary` interface (`useArticles.ts:78-88`):
`id`, `topic`, `title`, `requestedBy`, `generatedAt`, `precisionScore`, `styleScore`, `feedbackComment`, `hasFeedback`. Source mapping confirmed in Data Flow above.

### Amendment #3 — Include `stats` and `totalPages` in the response-level projection
The spec's example for `useArticleFeedbackListQuery` shows `articles`, `page`, `pageSize`, `totalCount` only. The local `ArticleFeedbackListResponse` interface also declares `totalPages` and `stats` (required), and `useArticleFeedbackAdapter.ts:27-34, 40-41` reads them. Both MUST be projected — and `stats` MUST be coalesced to a safe default if the generated DTO returns it as `undefined` (see Decision 2).

### Amendment #4 — Document `feedbackComment` projection rationale at the call site
Per Decision 3, add a single short comment at the `feedbackComment: null` projection line explaining that the backend list endpoint never emits this field (only `hasComment` is sent), so projecting `null` is exact behavior preservation. One sentence; non-obvious without reading the backend handler.

### Amendment #5 — Parameter name clarification
The spec writes `params.descending` when passing the 4th positional argument to `articles_FeedbackList`. This is correct (the local `ArticleFeedbackListParams` interface calls it `descending` per `useArticles.ts:73`), but the generated method parameter is named `sortDescending`. No code change; just be aware reviewers may flag the naming difference.

## Prerequisites

None blocking. All of the following are already true and were verified by reading the source:

- ✅ Generated client exposes `articles_GetTrace` (api-client.ts:479), `articles_FeedbackList` (api-client.ts:601), `articles_GetById` (api-client.ts:442).
- ✅ Generated `GetArticleResponse` declares `precisionScore`, `styleScore`, `feedbackComment` (api-client.ts:12879-12881).
- ✅ Generated `GetArticleTraceResponse` and `ArticleGenerationStepDto` are present and correctly typed (api-client.ts:13032-13151).
- ✅ Generated `GetArticleFeedbackListResponse` is present with `items` (not `articles`), `stats`, pagination fields (api-client.ts:13351-13410). Per-item DTO `ArticleFeedbackSummary` has `hasComment` (not `hasFeedback`) and lacks `feedbackComment` (api-client.ts:13412-13474) — confirmed against backend handler.
- ✅ `getAuthenticatedApiClient()` already imported by both target files (`useArticleTrace.ts:2`, `useArticles.ts:2`).
- ✅ No backend changes required.
- ✅ No client regeneration required.
- ✅ No new npm packages.
- ✅ No DB migration.
- ✅ No infrastructure change.

Implementation can start immediately.