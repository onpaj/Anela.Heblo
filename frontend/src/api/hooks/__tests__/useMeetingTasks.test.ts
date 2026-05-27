import React from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import {
  useMeetingTasksList,
  useMeetingTaskDetail,
  useUpdateProposedTaskStatus,
  useExplainMeetingSummary,
  MEETING_TASKS_KEYS,
  TranscriptListResponse,
  TranscriptDetailResponse,
} from '../useMeetingTasks';
import {
  createMockApiClient,
  mockAuthenticatedApiClient,
  createQueryClientWrapper,
} from '../../testUtils';

jest.mock('../../client');

describe('useMeetingTasks', () => {
  let mockFetch: jest.Mock;
  let mockClient: ReturnType<typeof createMockApiClient>['mockClient'];

  beforeEach(() => {
    const mock = createMockApiClient();
    mockClient = mock.mockClient;
    mockFetch = mock.mockFetch;
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('MEETING_TASKS_KEYS', () => {
    it('exposes stable list and detail key factories', () => {
      expect(MEETING_TASKS_KEYS.list).toEqual(['meetingTasks']);
      expect(MEETING_TASKS_KEYS.detail('abc')).toEqual(['meetingTasks', 'abc']);
    });
  });

  describe('useMeetingTasksList', () => {
    it('GETs /api/meeting-tasks with statusFilter, pageNumber, pageSize', async () => {
      const payload: TranscriptListResponse = {
        success: true,
        items: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 20,
        totalPages: 0,
      };
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });

      const { wrapper } = createQueryClientWrapper();
      const { result } = renderHook(
        () => useMeetingTasksList('PendingReview', undefined, false, 2, 20),
        { wrapper },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(mockFetch).toHaveBeenCalledWith(
        `${mockClient.baseUrl}/api/meeting-tasks?statusFilter=PendingReview&pageNumber=2&pageSize=20`,
        expect.objectContaining({ method: 'GET' }),
      );
    });

    it('omits statusFilter param when filter is undefined', async () => {
      mockFetch.mockResolvedValue({
        ok: true, status: 200,
        json: () => Promise.resolve({ success: true, items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }),
      });
      const { wrapper } = createQueryClientWrapper();
      renderHook(() => useMeetingTasksList(undefined, undefined, false, 1, 20), { wrapper });
      await waitFor(() => expect(mockFetch).toHaveBeenCalled());
      expect(mockFetch).toHaveBeenCalledWith(
        `${mockClient.baseUrl}/api/meeting-tasks?pageNumber=1&pageSize=20`,
        expect.any(Object),
      );
    });

    it('throws "API error: {status}" on non-2xx', async () => {
      mockFetch.mockResolvedValue({ ok: false, status: 500, json: () => Promise.resolve({}) });
      const { wrapper } = createQueryClientWrapper();
      const { result } = renderHook(() => useMeetingTasksList(undefined, undefined, false, 1, 20), { wrapper });
      await waitFor(() => expect(result.current.isError).toBe(true));
      expect((result.current.error as Error).message).toBe('API error: 500');
    });

    it('uses refetchOnMount "always" — refetches even when data is fresh on remount', async () => {
      const payload: TranscriptListResponse = {
        success: true, items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0,
      };
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });

      // Use a high staleTime so data stays "fresh" after the first fetch.
      // With refetchOnMount: "always", a remount still triggers a fetch even on fresh data.
      // With the default refetchOnMount: true, a remount on fresh data would NOT refetch.
      const { QueryClient: QC, QueryClientProvider: QCP } = require('@tanstack/react-query');
      const freshClient = new QC({
        defaultOptions: { queries: { retry: false, staleTime: Infinity } },
      });
      const freshWrapper = ({ children }: { children: React.ReactNode }) =>
        React.createElement(QCP, { client: freshClient }, children);

      const { result, unmount } = renderHook(() => useMeetingTasksList(undefined, undefined, false, 1, 20), { wrapper: freshWrapper });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mockFetch).toHaveBeenCalledTimes(1);

      unmount();
      const { result: result2 } = renderHook(() => useMeetingTasksList(undefined, undefined, false, 1, 20), { wrapper: freshWrapper });
      await waitFor(() => expect(result2.current.isSuccess).toBe(true));
      expect(mockFetch).toHaveBeenCalledTimes(2);
    });
  });

  describe('useMeetingTaskDetail', () => {
    it('does not fetch when id is empty', () => {
      const { wrapper } = createQueryClientWrapper();
      renderHook(() => useMeetingTaskDetail(''), { wrapper });
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it('GETs /api/meeting-tasks/{id} when id is provided', async () => {
      const payload: TranscriptDetailResponse = {
        success: true,
        transcript: {
          id: 'abc',
          subject: 'Test',
          summary: '',
          plaudRecordingId: 'r',
          plaudCreatedAt: '2026-05-13T08:00:00Z',
          status: 'PendingReview',
          receivedAt: '2026-05-13T08:00:00Z',
          reviewedAt: null,
          reviewedByUser: null,
          taskCount: 0,
          approvedTaskCount: 0,
          rejectedTaskCount: 0,
          tasks: [],
        },
      };
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });
      const { wrapper } = createQueryClientWrapper();
      const { result } = renderHook(() => useMeetingTaskDetail('abc'), { wrapper });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mockFetch).toHaveBeenCalledWith(
        `${mockClient.baseUrl}/api/meeting-tasks/abc`,
        expect.objectContaining({ method: 'GET' }),
      );
    });
  });

  describe('useUpdateProposedTaskStatus', () => {
    it('invalidates detail and list on success', async () => {
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve({}) });
      const { wrapper, queryClient } = createQueryClientWrapper();
      const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');

      const { result } = renderHook(() => useUpdateProposedTaskStatus(), { wrapper });
      await result.current.mutateAsync({ transcriptId: 'tid', taskId: 'k', status: 'Approved' });

      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: MEETING_TASKS_KEYS.detail('tid') });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: MEETING_TASKS_KEYS.list });
    });
  });

  describe('useExplainMeetingSummary', () => {
    it('POSTs to /api/meeting-tasks/{id}/explain with selectedText', async () => {
      const payload = {
        success: true,
        relevantTranscript: 'slice of transcript',
        explanation: 'because of X',
      };
      mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });
      const { wrapper } = createQueryClientWrapper();
      const { result } = renderHook(() => useExplainMeetingSummary(), { wrapper });

      await result.current.mutateAsync({
        transcriptId: 'some-id',
        selectedText: 'selected fragment',
      });

      expect(mockFetch).toHaveBeenCalledWith(
        `${mockClient.baseUrl}/api/meeting-tasks/some-id/explain`,
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ selectedText: 'selected fragment' }),
        }),
      );
    });
  });
});
