// frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts
import { renderHook } from '@testing-library/react';
import * as articleHooks from '../../../../api/hooks/useArticles';
import { useArticleFeedbackAdapter } from '../useArticleFeedbackAdapter';
import type { GenericFeedbackParams } from '../../types';

jest.mock('../../../../api/hooks/useArticles');

const mockArticle = {
  id: 'art-1',
  topic: 'Péče o pleť v létě',
  title: 'Jak pečovat o pleť v letních měsících',
  requestedBy: 'user@anela.cz',
  generatedAt: '2026-01-15T10:30:00Z',
  precisionScore: 4,
  styleScore: 5,
  feedbackComment: 'Skvělý článek.',
  hasFeedback: true,
};

const mockArticleNoTitle = {
  id: 'art-2',
  topic: 'Zimní vlasová péče',
  title: null,
  requestedBy: 'other@anela.cz',
  generatedAt: null,
  precisionScore: null,
  styleScore: null,
  feedbackComment: null,
  hasFeedback: false,
};

const mockStats = {
  totalArticles: 30,
  totalWithFeedback: 5,
  avgPrecisionScore: 3.9,
  avgStyleScore: 4.0,
};

const params: GenericFeedbackParams = {
  pageNumber: 1, pageSize: 20, sortBy: 'CreatedAt', sortDescending: true, userId: 'user@anela.cz',
};

beforeEach(() => {
  jest.spyOn(articleHooks, 'useArticleFeedbackListQuery').mockReturnValue({
    data: {
      articles: [mockArticle, mockArticleNoTitle],
      totalCount: 30,
      page: 1,
      pageSize: 20,
      totalPages: 2,
      stats: mockStats,
    },
    isLoading: false,
    isError: false,
  } as any);
});

afterEach(() => jest.restoreAllMocks());

test('maps title to primaryText when title is present', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].primaryText).toBe('Jak pečovat o pleť v letních měsících');
});

test('falls back to topic as primaryText when title is null', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[1].primaryText).toBe('Zimní vlasová péče');
});

test('maps topic to secondaryText', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].secondaryText).toBe('Péče o pleť v létě');
});

test('maps requestedBy to userId', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].userId).toBe('user@anela.cz');
});

test('maps generatedAt to createdAt', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].createdAt).toBe('2026-01-15T10:30:00Z');
});

test('uses empty string for createdAt when generatedAt is null', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[1].createdAt).toBe('');
});

test('maps totalArticles to totalItems in stats', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.stats?.totalItems).toBe(30);
});

test('translates GenericFeedbackParams to article-specific params', () => {
  renderHook(() => useArticleFeedbackAdapter(params));
  expect(articleHooks.useArticleFeedbackListQuery).toHaveBeenCalledWith(
    expect.objectContaining({
      page: 1,
      pageSize: 20,
      sortBy: 'CreatedAt',
      sortDescending: true,
      requestedBy: 'user@anela.cz',
    }),
  );
});

test('returns empty rows and undefined stats when loading', () => {
  jest.spyOn(articleHooks, 'useArticleFeedbackListQuery').mockReturnValue({
    data: undefined, isLoading: true, isError: false,
  } as any);
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows).toEqual([]);
  expect(result.current.stats).toBeUndefined();
  expect(result.current.isLoading).toBe(true);
});

test('returns isError and empty rows when query errors', () => {
  jest.spyOn(articleHooks, 'useArticleFeedbackListQuery').mockReturnValue({
    data: undefined, isLoading: false, isError: true,
  } as any);
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.isError).toBe(true);
  expect(result.current.rows).toEqual([]);
});
