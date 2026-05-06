import { renderHook } from '@testing-library/react';
import * as leafletHooks from '../../../../api/hooks/useLeaflet';
import { useLeafletFeedbackAdapter } from '../useLeafletFeedbackAdapter';
import type { GenericFeedbackParams } from '../../types';

jest.mock('../../../../api/hooks/useLeaflet');

const mockItem = {
  id: 'gen-1',
  topic: 'Letní kolekce 2026',
  audience: 'ženy 25-40',
  length: 'medium',
  finalMarkdown: 'Tato letní kolekce přináší svěží barvy a moderní střihy pro ženy všech věkových kategorií.',
  kbSourceCount: 2,
  leafletSourceCount: 3,
  durationMs: 5000,
  createdAt: '2026-01-15T10:30:00Z',
  userId: 'user@anela.cz',
  precisionScore: 5,
  styleScore: 4,
  feedbackComment: null,
  hasFeedback: true,
};

const mockStats = {
  totalGenerations: 50,
  totalWithFeedback: 12,
  avgPrecisionScore: 4.2,
  avgStyleScore: null,
};

const params: GenericFeedbackParams = {
  pageNumber: 2, pageSize: 10, sortBy: 'CreatedAt', sortDescending: false,
};

beforeEach(() => {
  jest.spyOn(leafletHooks, 'useLeafletFeedbackListQuery').mockReturnValue({
    data: { items: [mockItem], totalCount: 50, pageNumber: 2, pageSize: 10, totalPages: 5, stats: mockStats },
    isLoading: false,
    isError: false,
  } as any);
});

afterEach(() => jest.restoreAllMocks());

test('maps topic to primaryText', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.rows[0].primaryText).toBe('Letní kolekce 2026');
});

test('maps truncated finalMarkdown to secondaryText', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.rows[0].secondaryText).toBe(mockItem.finalMarkdown.slice(0, 120));
});

test('maps totalGenerations to totalItems in stats', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.stats?.totalItems).toBe(50);
});

test('calculates totalPages from totalCount and pageSize', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  // 50 items / 10 per page = 5 pages
  expect(result.current.totalPages).toBe(5);
});

test('passes GenericFeedbackParams to query (translates field names)', () => {
  renderHook(() => useLeafletFeedbackAdapter(params));
  expect(leafletHooks.useLeafletFeedbackListQuery).toHaveBeenCalledWith(
    expect.objectContaining({ pageNumber: 2, pageSize: 10, sortBy: 'CreatedAt', sortDescending: false }),
  );
});

test('hasFeedback is true when at least one score is present', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  // mockItem has precisionScore: 5 and styleScore: 4
  expect(result.current.rows[0].hasFeedback).toBe(true);
});

test('hasFeedback is false when both scores are null', () => {
  jest.spyOn(leafletHooks, 'useLeafletFeedbackListQuery').mockReturnValue({
    data: {
      items: [{ ...mockItem, precisionScore: null, styleScore: null, hasFeedback: false }],
      totalCount: 1, pageNumber: 2, pageSize: 10, totalPages: 1, stats: mockStats,
    },
    isLoading: false,
    isError: false,
  } as any);
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.rows[0].hasFeedback).toBe(false);
});

test('returns empty rows and undefined stats when loading', () => {
  jest.spyOn(leafletHooks, 'useLeafletFeedbackListQuery').mockReturnValue({
    data: undefined, isLoading: true, isError: false,
  } as any);
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.rows).toEqual([]);
  expect(result.current.stats).toBeUndefined();
  expect(result.current.isLoading).toBe(true);
});
