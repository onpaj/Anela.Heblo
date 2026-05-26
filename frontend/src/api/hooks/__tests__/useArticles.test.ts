import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useArticleFeedbackListQuery } from '../useArticles';
import * as clientModule from '../../client';

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
