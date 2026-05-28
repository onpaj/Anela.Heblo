---

# Implementation: Remove Private API Client Access in `useSubmitArticleFeedbackMutation`

## What was implemented

Replaced the `(apiClient as any).http.fetch` + hand-rolled URL/body pattern in `useSubmitArticleFeedbackMutation` with a typed call to the generated `apiClient.articles_SubmitFeedback(articleId, request)`. HTTP 409 is detected via `SwaggerException.isSwaggerException(err) && err.status === 409` and resolves to `{ alreadySubmitted: true }`. Passes `getAuthenticatedApiClient(false)` to suppress the global error toast for this expected business-conflict status. Removed the `TODO(arch-review 2026-05-25)` comment block.

Note: The sandbox blocks all writes to source directories and all git operations require interactive approval. The implementation is specified below in full — the harness applies it to the codebase.

## Files created/modified

- `frontend/src/api/hooks/useArticles.ts` — update imports (add `SubmitArticleFeedbackRequest`, `SwaggerException`); replace `mutationFn` body in `useSubmitArticleFeedbackMutation` with typed call; remove TODO comment
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — add `useSubmitArticleFeedbackMutation` and `SubmitArticleFeedbackResponse`/`SwaggerException` imports; add `describe('useSubmitArticleFeedbackMutation')` block with 3 tests

## Full File Contents

### `frontend/src/api/hooks/useArticles.ts` (complete)

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  ArticleStatus,
  GenerateArticleRequest,
  SubmitArticleFeedbackRequest,
  SwaggerException,
} from '../generated/api-client';

// ---- Types ----

export interface ArticleListItem {
  id: string;
  topic: string;
  title: string | null;
  status: ArticleStatus;
  createdAt: string;
  generatedAt: string | null;
}

export interface ArticleSource {
  title: string;
  url: string | null;
  type: string;
  knowledgeBaseChunkId: string | null;
  confidence: number | null;
  excerpt: string | null;
  validationNote: string | null;
}

export interface ArticleDetail {
  id: string;
  topic: string;
  scope: string;
  audience: string | null;
  angle: string | null;
  length: string;
  title: string | null;
  htmlContent: string | null;
  status: ArticleStatus;
  errorMessage: string | null;
  createdAt: string;
  generatedAt: string | null;
  useKnowledgeBase: boolean;
  useWebSearch: boolean;
  sources: ArticleSource[];
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
}

export interface ListArticlesParams {
  status?: ArticleStatus;
  page?: number;
  pageSize?: number;
}

export interface SubmitArticleFeedbackPayload {
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface SubmitArticleFeedbackResult {
  alreadySubmitted?: true;
  precisionScore?: number | null;
  styleScore?: number | null;
  feedbackComment?: string | null;
}

export interface ArticleFeedbackListParams {
  hasFeedback?: boolean;
  requestedBy?: string;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ArticleFeedbackSummary {
  id: string;
  topic: string;
  title: string | null;
  requestedBy: string;
  createdAt: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  hasComment: boolean;
}

export interface ArticleFeedbackStats {
  totalArticles: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

export interface ArticleFeedbackListResponse {
  articles: ArticleFeedbackSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  stats: ArticleFeedbackStats;
}

export const IN_PROGRESS_STATUSES = new Set<ArticleStatus>([
  ArticleStatus.Queued,
  ArticleStatus.Researching,
  ArticleStatus.Writing,
]);

// ---- Query key factory ----

export const articleKeys = {
  all: [...QUERY_KEYS.articles] as const,
  list: (params?: ListArticlesParams) =>
    [...QUERY_KEYS.articles, 'list', params ?? {}] as const,
  detail: (id: string) => [...QUERY_KEYS.articles, 'detail', id] as const,
  feedbackList: (params?: ArticleFeedbackListParams) =>
    [...QUERY_KEYS.articles, 'feedback-list', params ?? {}] as const,
};

// ---- Hooks ----

export const useListArticlesQuery = (params: ListArticlesParams = {}) => {
  return useQuery({
    queryKey: articleKeys.list(params),
    queryFn: async (): Promise<ArticleListItem[]> => {
      const client = getAuthenticatedApiClient();
      const response = await client.articles_List(
        params.status ?? null,
        params.page ?? 1,
        params.pageSize ?? 20,
      );
      return (response.items ?? []).map((item) => ({
        id: item.id?.toString() ?? '',
        topic: item.topic ?? '',
        title: item.title ?? null,
        status: item.status ?? ArticleStatus.Queued,
        createdAt: item.createdAt?.toISOString() ?? '',
        generatedAt: item.generatedAt?.toISOString() ?? null,
      }));
    },
    staleTime: 30 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

export const useGetArticleQuery = (id: string | null) => {
  return useQuery({
    queryKey: articleKeys.detail(id ?? ''),
    queryFn: async (): Promise<ArticleDetail> => {
      const client = getAuthenticatedApiClient();
      const response = await client.articles_GetById(id!);
      return {
        id: response.id?.toString() ?? '',
        topic: response.topic ?? '',
        scope: response.scope ?? '',
        audience: response.audience ?? null,
        angle: response.angle ?? null,
        length: response.length ?? '',
        title: response.title ?? null,
        htmlContent: response.htmlContent ?? null,
        status: response.status ?? ArticleStatus.Queued,
        errorMessage: response.errorMessage ?? null,
        createdAt: response.createdAt?.toISOString() ?? '',
        generatedAt: response.generatedAt?.toISOString() ?? null,
        useKnowledgeBase: response.useKnowledgeBase ?? false,
        useWebSearch: response.useWebSearch ?? false,
        sources: (response.sources ?? []).map((s) => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const raw = s as any;
          return {
            title: s.title ?? '',
            url: s.url ?? null,
            type: s.type ?? '',
            knowledgeBaseChunkId: (raw.knowledgeBaseChunkId as string | null) ?? null,
            confidence: (raw.confidence as number | null) ?? null,
            excerpt: (raw.excerpt as string | null) ?? null,
            validationNote: (raw.validationNote as string | null) ?? null,
          };
        }),
        precisionScore: response.precisionScore ?? null,
        styleScore: response.styleScore ?? null,
        feedbackComment: response.feedbackComment ?? null,
      };
    },
    enabled: !!id,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status && IN_PROGRESS_STATUSES.has(status) ? 3000 : false;
    },
    staleTime: 10 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

export const useGenerateArticleMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: GenerateArticleRequest): Promise<string> => {
      const client = getAuthenticatedApiClient();
      const response = await client.articles_Generate(request);
      return response.articleId?.toString() ?? '';
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: articleKeys.all });
    },
  });
};

export const useSubmitArticleFeedbackMutation = (articleId: string) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
      // Suppress global "Chyba API (409)" toast — 409 is an expected
      // business outcome that resolves with { alreadySubmitted: true }.
      const apiClient = getAuthenticatedApiClient(false);

      const request = new SubmitArticleFeedbackRequest({
        articleId,
        precisionScore: payload.precisionScore,
        styleScore: payload.styleScore,
        comment: payload.comment,
      });

      try {
        const r = await apiClient.articles_SubmitFeedback(articleId, request);
        return {
          precisionScore: r.precisionScore ?? null,
          styleScore: r.styleScore ?? null,
          feedbackComment: r.feedbackComment ?? null,
        };
      } catch (err) {
        if (SwaggerException.isSwaggerException(err) && err.status === 409) {
          return { alreadySubmitted: true };
        }
        throw err;
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: articleKeys.detail(articleId) });
    },
  });
};

export const useArticleFeedbackListQuery = (params: ArticleFeedbackListParams = {}) => {
  return useQuery({
    queryKey: articleKeys.feedbackList(params),
    queryFn: async (): Promise<ArticleFeedbackListResponse> => {
      const client = getAuthenticatedApiClient();
      const data = await client.articles_FeedbackList(
        params.hasFeedback ?? null,
        params.requestedBy ?? null,
        params.sortBy,
        params.sortDescending,
        params.page,
        params.pageSize,
      );
      return {
        articles: (data.items ?? []).map((item) => ({
          id: item.id ?? '',
          topic: item.topic ?? '',
          title: item.title ?? null,
          requestedBy: item.requestedBy ?? '',
          createdAt: item.createdAt?.toISOString() ?? null,
          precisionScore: item.precisionScore ?? null,
          styleScore: item.styleScore ?? null,
          hasComment: item.hasComment ?? false,
        })),
        totalCount: data.totalCount ?? 0,
        page: data.page ?? params.page ?? 1,
        pageSize: data.pageSize ?? params.pageSize ?? 20,
        totalPages: data.totalPages ?? 0,
        stats: data.stats
          ? {
              totalArticles: data.stats.totalArticles ?? 0,
              totalWithFeedback: data.stats.totalWithFeedback ?? 0,
              avgPrecisionScore: data.stats.avgPrecisionScore ?? null,
              avgStyleScore: data.stats.avgStyleScore ?? null,
            }
          : {
              totalArticles: 0,
              totalWithFeedback: 0,
              avgPrecisionScore: null,
              avgStyleScore: null,
            },
      };
    },
    staleTime: 30_000,
    gcTime: 5 * 60 * 1000,
  });
};
```

### `frontend/src/api/hooks/__tests__/useArticles.test.ts` (complete)

```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useArticleFeedbackListQuery, useSubmitArticleFeedbackMutation } from '../useArticles';
import * as clientModule from '../../client';
import {
  SubmitArticleFeedbackResponse,
  SwaggerException,
} from '../../generated/api-client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    articles: ['articles'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(
    QueryClientProvider,
    { client: queryClient },
    children,
  );
};

describe('useArticleFeedbackListQuery mapping', () => {
  let mockArticlesFeedbackList: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockArticlesFeedbackList = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      articles_FeedbackList: mockArticlesFeedbackList,
    } as any);
  });

  it('maps a populated DTO row to the renamed frontend shape (createdAt, hasComment, no feedbackComment)', async () => {
    mockArticlesFeedbackList.mockResolvedValue({
      items: [
        {
          id: 'art-1',
          topic: 'Topic A',
          title: 'Title A',
          requestedBy: 'user@anela.cz',
          createdAt: new Date('2026-01-15T10:30:00Z'),
          precisionScore: 4,
          styleScore: 5,
          hasComment: true,
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
      stats: {
        totalArticles: 1,
        totalWithFeedback: 1,
        avgPrecisionScore: 4,
        avgStyleScore: 5,
      },
    });

    const { result } = renderHook(() => useArticleFeedbackListQuery({}), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data!.articles).toHaveLength(1);
    expect(result.current.data!.articles[0]).toEqual({
      id: 'art-1',
      topic: 'Topic A',
      title: 'Title A',
      requestedBy: 'user@anela.cz',
      createdAt: '2026-01-15T10:30:00.000Z',
      precisionScore: 4,
      styleScore: 5,
      hasComment: true,
    });
    expect(result.current.data!.totalCount).toBe(1);
    expect(result.current.data!.stats).toEqual({
      totalArticles: 1,
      totalWithFeedback: 1,
      avgPrecisionScore: 4,
      avgStyleScore: 5,
    });
  });

  it('returns empty articles and default stats when items and stats are missing from the DTO', async () => {
    mockArticlesFeedbackList.mockResolvedValue({
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0,
    });

    const { result } = renderHook(() => useArticleFeedbackListQuery({}), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data!.articles).toEqual([]);
    expect(result.current.data!.stats).toEqual({
      totalArticles: 0,
      totalWithFeedback: 0,
      avgPrecisionScore: null,
      avgStyleScore: null,
    });
  });

  it('preserves hasComment per row (does not synthesize it from other fields) and maps null createdAt', async () => {
    mockArticlesFeedbackList.mockResolvedValue({
      items: [
        {
          id: 'art-with-comment',
          topic: 'T',
          title: null,
          requestedBy: 'u',
          createdAt: undefined,
          precisionScore: null,
          styleScore: null,
          hasComment: true,
        },
        {
          id: 'art-without-comment',
          topic: 'T',
          title: null,
          requestedBy: 'u',
          createdAt: undefined,
          precisionScore: null,
          styleScore: null,
          hasComment: false,
        },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    });

    const { result } = renderHook(() => useArticleFeedbackListQuery({}), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const [withComment, withoutComment] = result.current.data!.articles;
    expect(withComment.hasComment).toBe(true);
    expect(withComment.createdAt).toBeNull();
    expect(withoutComment.hasComment).toBe(false);
    expect(withoutComment.createdAt).toBeNull();
  });
});

describe('useArticleFeedbackListQuery parameter passing', () => {
  let mockFeedbackList: jest.Mock;

  const emptyClientResponse = {
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
    totalPages: 0,
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockFeedbackList = jest.fn().mockResolvedValue(emptyClientResponse);
    mockGetAuthenticatedApiClient.mockReturnValue({
      articles_FeedbackList: mockFeedbackList,
    } as any);
  });

  it('passes sortDescending=true to the API client (not descending)', async () => {
    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: true }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFeedbackList).toHaveBeenCalledWith(
      null,      // hasFeedback
      null,      // requestedBy
      undefined, // sortBy
      true,      // sortDescending
      undefined, // page
      undefined, // pageSize
    );
  });

  it('passes sortDescending=false when toggled off', async () => {
    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortDescending: false }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFeedbackList).toHaveBeenCalledWith(
      null,
      null,
      undefined,
      false,
      undefined,
      undefined,
    );
  });

  it('passes sortDescending=undefined when not specified (backend default applies)', async () => {
    const { result } = renderHook(
      () => useArticleFeedbackListQuery({ sortBy: 'CreatedAt' }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFeedbackList).toHaveBeenCalledWith(
      null,
      null,
      'CreatedAt',
      undefined,
      undefined,
      undefined,
    );
  });

  it('passes all filter params including sortDescending to the API client', async () => {
    const { result } = renderHook(
      () =>
        useArticleFeedbackListQuery({
          hasFeedback: true,
          requestedBy: 'user@anela.cz',
          sortBy: 'CreatedAt',
          sortDescending: false,
          page: 2,
          pageSize: 10,
        }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFeedbackList).toHaveBeenCalledWith(
      true,
      'user@anela.cz',
      'CreatedAt',
      false,
      2,
      10,
    );
  });
});

describe('useSubmitArticleFeedbackMutation', () => {
  const articleId = 'article-123';
  let mockArticlesSubmitFeedback: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockArticlesSubmitFeedback = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      articles_FeedbackList: jest.fn(),
      articles_SubmitFeedback: mockArticlesSubmitFeedback,
    } as any);
  });

  it('resolves with mapped scores and comment on 2xx', async () => {
    const response = new SubmitArticleFeedbackResponse({
      precisionScore: 4,
      styleScore: 5,
      feedbackComment: 'Helpful.',
    });
    mockArticlesSubmitFeedback.mockResolvedValueOnce(response);

    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation(articleId),
      { wrapper: createWrapper },
    );

    const mutationResult = await result.current.mutateAsync({
      precisionScore: 4,
      styleScore: 5,
      comment: 'Helpful.',
    });

    expect(mockArticlesSubmitFeedback).toHaveBeenCalledTimes(1);
    expect(mockArticlesSubmitFeedback).toHaveBeenCalledWith(
      articleId,
      expect.objectContaining({
        articleId,
        precisionScore: 4,
        styleScore: 5,
        comment: 'Helpful.',
      }),
    );
    expect(mutationResult).toEqual({
      precisionScore: 4,
      styleScore: 5,
      feedbackComment: 'Helpful.',
    });
  });

  it('resolves with { alreadySubmitted: true } when the server returns 409', async () => {
    const conflict = new SwaggerException(
      'Already submitted',
      409,
      '',
      {},
      null,
    );
    mockArticlesSubmitFeedback.mockRejectedValueOnce(conflict);

    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation(articleId),
      { wrapper: createWrapper },
    );

    const mutationResult = await result.current.mutateAsync({
      precisionScore: 3,
      styleScore: 3,
      comment: '',
    });

    expect(mutationResult).toEqual({ alreadySubmitted: true });
  });

  it('rejects when the server returns a non-409 error', async () => {
    const serverError = new SwaggerException(
      'Internal server error',
      500,
      '',
      {},
      null,
    );
    mockArticlesSubmitFeedback.mockRejectedValueOnce(serverError);

    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation(articleId),
      { wrapper: createWrapper },
    );

    await expect(
      result.current.mutateAsync({
        precisionScore: 1,
        styleScore: 1,
        comment: '',
      }),
    ).rejects.toBe(serverError);
  });
});
```

## Tests

`frontend/src/api/hooks/__tests__/useArticles.test.ts` — adds `describe('useSubmitArticleFeedbackMutation')` with:
- `'resolves with mapped scores and comment on 2xx'` — mocks `articles_SubmitFeedback` returning `SubmitArticleFeedbackResponse`; asserts call args and mapped result
- `'resolves with { alreadySubmitted: true } when the server returns 409'` — mocks rejection with `SwaggerException(status=409)`; asserts `{ alreadySubmitted: true }` resolution
- `'rejects when the server returns a non-409 error'` — mocks rejection with `SwaggerException(status=500)`; asserts mutation rejects with the original error

## How to verify

```bash
cd frontend
npx jest src/api/hooks/__tests__/useArticles.test.ts --no-coverage
npx tsc --noEmit
npx eslint src/api/hooks/useArticles.ts src/api/hooks/__tests__/useArticles.test.ts
```

File the FR-6 GitHub issue:
```bash
gh issue create \
  --title "Convert sibling feedback-submit hooks + docs to typed SwaggerException pattern (follow-up to arch-review 2026-05-25)" \
  --body "This issue tracks the broader rollout of the pattern applied to useSubmitArticleFeedbackMutation in useArticles.ts. That change replaced (apiClient as any).http.fetch + hand-rolled URL/body with a typed call to articles_SubmitFeedback and SwaggerException.isSwaggerException(err) && err.status === 409 for the 409 branch.

Remaining work:
1. Convert useKnowledgeBase.useSubmitFeedbackMutation (lines 387-410) and useLeaflet feedback submit (lines 286-315) to the same try/catch SwaggerException pattern.
2. Update docs/development/api-client-generation.md and memory/gotchas/api-client-must-use-absolute-urls.md to recommend typed methods + SwaggerException for status branching, not (apiClient as any).baseUrl.
3. Add expectedStatuses?: number[] escape hatch to getAuthenticatedApiClient so mutations can opt out of global toasts for specific status codes without disabling them wholesale.
4. (Long-term) Change NSwag template for endpoints where 409 is a business outcome to return a typed discriminated result, eliminating try/catch.

Context: (apiClient as any).baseUrl / .http.fetch is used by 34 hook files. Items 1 and 2 should land together. Original audit: arch-review 2026-05-25 in useArticles.ts (now removed). Endpoint: POST /api/articles/{articleId}/feedback."
```

Recommended commit message:
```
refactor(articles): use generated typed feedback submission, drop as-any

Replace (apiClient as any).http.fetch + hand-rolled URL/body in
useSubmitArticleFeedbackMutation with apiClient.articles_SubmitFeedback.
Detect 409 via SwaggerException.isSwaggerException(err) && err.status === 409,
preserving { alreadySubmitted: true } result. Suppress global error toast
(getAuthenticatedApiClient(false)) since 409 is an expected business outcome.
Removes TODO(arch-review 2026-05-25).

Refs: arch-review 2026-05-25
```

## Notes

- Sandbox blocks all writes to `frontend/src/` and all git operations require interactive approval; implementation is specified here for the harness to apply.
- `SubmitArticleFeedbackResponse` constructor inherits `BaseResponse`'s copy-all pattern, so `new SubmitArticleFeedbackResponse({ precisionScore, styleScore, feedbackComment })` correctly sets those fields.
- `SwaggerException.isSwaggerException` uses a protected instance property (`isSwaggerException === true`) rather than `instanceof`, which is safe across test/module-resolution boundaries.
- Consumer `frontend/src/features/articles/ArticleFeedbackSection.tsx` accesses `result.alreadySubmitted` and `submitFeedback.data?.alreadySubmitted` — both preserved exactly. No consumer change required.
- The `getAuthenticatedApiClient(false)` call is scoped to this mutation only; all other hooks continue to use the default `true` (toasts on).

## PR Summary

Remove the `(apiClient as any).http` / `(apiClient as any).baseUrl` bypass in `useSubmitArticleFeedbackMutation`, which accessed private NSwag-generated internals to hand-roll the feedback submission request. The fix uses the generated `articles_SubmitFeedback` typed method directly, detecting HTTP 409 ("already submitted") via the publicly exported `SwaggerException.isSwaggerException(err) && err.status === 409` guard instead of branching on `response.status`. This preserves the externally visible `{ alreadySubmitted: true }` result for consumers.

A latent bug is also fixed: the previous implementation routed 409 through `authenticatedHttp.fetch`, which fired the global "Chyba API (409)" toast before the hook could branch. Passing `getAuthenticatedApiClient(false)` suppresses the toast for this mutation only, since the consumer (`ArticleFeedbackSection.tsx`) already renders inline error text.

The PR intentionally deviates from `docs/development/api-client-generation.md` for this one hook; the doc update and sibling-hook rollout (`useKnowledgeBase.useSubmitFeedbackMutation`, `useLeaflet` feedback submit) are tracked in the FR-6 follow-up issue.

### Changes
- `frontend/src/api/hooks/useArticles.ts` — import `SubmitArticleFeedbackRequest` and `SwaggerException` from the generated client; replace `mutationFn` body with typed `articles_SubmitFeedback` call + `SwaggerException` catch; remove `TODO(arch-review 2026-05-25)` comment
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — add imports for `useSubmitArticleFeedbackMutation`, `SubmitArticleFeedbackResponse`, `SwaggerException`; add `describe('useSubmitArticleFeedbackMutation')` with 3 tests (2xx, 409, 500)

## Status
DONE_WITH_CONCERNS

**Concern:** The sandbox environment blocks all writes to `frontend/src/` and requires interactive approval for all git operations (checkout, add, commit, push). Tests could not be executed locally to confirm green. The implementation was verified through static analysis: generated client examined directly via `git show origin/main:...`, `SwaggerException` constructor and static guard confirmed, `SubmitArticleFeedbackRequest`/`SubmitArticleFeedbackResponse` constructors confirmed correct, consumer field usage confirmed unchanged. The harness must apply the file contents above and run tests before merging.