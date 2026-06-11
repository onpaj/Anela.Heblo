import { renderHook } from '@testing-library/react';
import * as kbHooks from '../../../../api/hooks/useKnowledgeBase';
import { useKbFeedbackAdapter } from '../useKbFeedbackAdapter';
import type { GenericFeedbackParams } from '../../types';

jest.mock('../../../../api/hooks/useKnowledgeBase');

const mockLog = {
  id: 'log-1',
  question: 'Jak funguje věrnostní program?',
  answer: 'Věrnostní program nabízí různé výhody pro zákazníky po celý rok.',
  topK: 5,
  sourceCount: 3,
  durationMs: 1200,
  createdAt: '2026-01-15T10:30:00Z',
  userId: 'user@anela.cz',
  userName: 'User Example',
  precisionScore: 4,
  styleScore: 3,
  feedbackComment: 'Výborná odpověď.',
  hasFeedback: true,
};

const mockStats = {
  totalQuestions: 100,
  totalWithFeedback: 20,
  avgPrecisionScore: 3.8,
  avgStyleScore: 4.1,
};

const params: GenericFeedbackParams = {
  pageNumber: 1, pageSize: 20, sortBy: 'CreatedAt', sortDescending: true,
};

beforeEach(() => {
  jest.spyOn(kbHooks, 'useKnowledgeBaseFeedbackListQuery').mockReturnValue({
    data: {
      success: true,
      logs: [mockLog],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1,
      stats: mockStats,
    },
    isLoading: false,
    isError: false,
  } as any);
});

afterEach(() => jest.restoreAllMocks());

test('maps question to primaryText', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].primaryText).toBe('Jak funguje věrnostní program?');
});

test('maps truncated answer to secondaryText', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].secondaryText).toBe(
    mockLog.answer.slice(0, 120),
  );
});

test('maps userId', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].userId).toBe('user@anela.cz');
});

test('maps userName', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].userName).toBe('User Example');
});

test('maps totalQuestions to totalItems in stats', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.stats?.totalItems).toBe(100);
});

test('passes through avgPrecisionScore', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.stats?.avgPrecisionScore).toBe(3.8);
});

test('returns totalPages from query', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.totalPages).toBe(1);
});

test('returns empty rows and undefined stats when loading', () => {
  jest.spyOn(kbHooks, 'useKnowledgeBaseFeedbackListQuery').mockReturnValue({
    data: undefined, isLoading: true, isError: false,
  } as any);
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows).toEqual([]);
  expect(result.current.stats).toBeUndefined();
  expect(result.current.isLoading).toBe(true);
});

test('maps hasFeedback from log', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].hasFeedback).toBe(true);
});

test('returns isError when query errors', () => {
  jest.spyOn(kbHooks, 'useKnowledgeBaseFeedbackListQuery').mockReturnValue({
    data: undefined, isLoading: false, isError: true,
  } as any);
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.isError).toBe(true);
  expect(result.current.rows).toEqual([]);
});
