import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import {
  useSubmitLeafletFeedbackMutation,
  useLeafletFeedbackListQuery,
} from '../useLeaflet';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    leaflet: ['leaflet'],
  },
}));

const mockUseMsal = jest.fn();
jest.mock('@azure/msal-react', () => ({
  useMsal: () => mockUseMsal(),
}));

const mockShouldUseMockAuth = jest.fn();
jest.mock('../../../config/runtimeConfig', () => ({
  shouldUseMockAuth: () => mockShouldUseMockAuth(),
}));

const mockGetUser = jest.fn();
jest.mock('../../../auth/mockAuth', () => ({
  mockAuthService: {
    getUser: () => mockGetUser(),
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
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const mockFetchResponse = (data: unknown, status = 200, ok = true) => ({
  ok,
  json: jest.fn().mockResolvedValue(data),
  status,
});

describe('useLeaflet hooks', () => {
  let mockHttp: { fetch: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockHttp = { fetch: jest.fn() };
    mockGetAuthenticatedApiClient.mockReturnValue({
      baseUrl: 'http://localhost:5001',
      http: mockHttp,
    } as any);
  });

  beforeEach(() => {
    mockUseMsal.mockReturnValue({ accounts: [], instance: {} as any, inProgress: 'none' as any });
    mockShouldUseMockAuth.mockReturnValue(false);
    mockGetUser.mockReturnValue(null);
  });

  describe('useSubmitLeafletFeedbackMutation', () => {
    it('sends POST request with feedback payload', async () => {
      mockHttp.fetch.mockResolvedValue(mockFetchResponse({}));

      const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        result.current.mutate({
          generationId: 'gen-123',
          precisionScore: 4,
          styleScore: 3,
          comment: 'Good answer',
        });
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockHttp.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/leaflet/feedback',
        expect.objectContaining({
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify({
            generationId: 'gen-123',
            precisionScore: 4,
            styleScore: 3,
            comment: 'Good answer',
          }),
        }),
      );
    });

    it('returns alreadySubmitted=true on 409 conflict', async () => {
      mockHttp.fetch.mockResolvedValue(mockFetchResponse({}, 409, false));

      const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        result.current.mutate({
          generationId: 'gen-456',
          precisionScore: 5,
          styleScore: 5,
        });
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data?.alreadySubmitted).toBe(true);
    });

    it('throws error on non-409 error status', async () => {
      mockHttp.fetch.mockResolvedValue(mockFetchResponse({}, 500, false));

      const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        result.current.mutate({
          generationId: 'gen-789',
          precisionScore: 2,
          styleScore: 2,
        });
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toEqual(
        new Error('Submit leaflet feedback failed: 500')
      );
    });
  });

  describe('useLeafletFeedbackListQuery', () => {
    it('fetches feedback list with no params', async () => {
      const mockData = {
        success: true,
        logs: [
          {
            id: 'gen-1',
            topic: 'Anti-aging serum',
            audience: 'professionals',
            length: 'medium',
            kbSourceCount: 2,
            leafletSourceCount: 1,
            durationMs: 1234,
            createdAt: '2026-05-01T10:00:00Z',
            userId: 'user-1',
            precisionScore: 4,
            styleScore: 5,
            feedbackComment: 'Great quality',
            hasFeedback: true,
          },
        ],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 20,
        totalPages: 1,
        stats: {
          totalGenerations: 10,
          totalWithFeedback: 3,
          avgPrecisionScore: 4.2,
          avgStyleScore: 4.5,
        },
      };
      mockHttp.fetch.mockResolvedValue(mockFetchResponse(mockData));

      const { result } = renderHook(() => useLeafletFeedbackListQuery(), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockHttp.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/leaflet/feedback/list',
        expect.objectContaining({ method: 'GET' }),
      );
      expect(result.current.data?.logs).toHaveLength(1);
      expect(result.current.data?.logs[0].topic).toBe('Anti-aging serum');
      expect(result.current.data?.stats.avgPrecisionScore).toBe(4.2);
    });

    it('builds URL with all filter params', async () => {
      const mockData = {
        success: true,
        logs: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 10,
        totalPages: 0,
        stats: {
          totalGenerations: 0,
          totalWithFeedback: 0,
          avgPrecisionScore: null,
          avgStyleScore: null,
        },
      };
      mockHttp.fetch.mockResolvedValue(mockFetchResponse(mockData));

      const { result } = renderHook(
        () =>
          useLeafletFeedbackListQuery({
            hasFeedback: true,
            userId: 'user-abc-123',
            sortBy: 'CreatedAt',
            sortDescending: true,
            pageNumber: 2,
            pageSize: 10,
          }),
        { wrapper: createWrapper },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      const calledUrl: string = mockHttp.fetch.mock.calls[0][0];
      expect(calledUrl).toContain('hasFeedback=true');
      expect(calledUrl).toContain('userId=user-abc-123');
      expect(calledUrl).toContain('sortBy=CreatedAt');
      expect(calledUrl).toContain('sortDescending=true');
      expect(calledUrl).toContain('pageNumber=2');
      expect(calledUrl).toContain('pageSize=10');
    });

    it('handles empty feedback list', async () => {
      const mockData = {
        success: true,
        logs: [],
        totalCount: 0,
        pageNumber: 1,
        pageSize: 20,
        totalPages: 0,
        stats: {
          totalGenerations: 0,
          totalWithFeedback: 0,
          avgPrecisionScore: null,
          avgStyleScore: null,
        },
      };
      mockHttp.fetch.mockResolvedValue(mockFetchResponse(mockData));

      const { result } = renderHook(() => useLeafletFeedbackListQuery(), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data?.logs).toHaveLength(0);
      expect(result.current.data?.stats.avgPrecisionScore).toBeNull();
    });

    it('throws error on failed request', async () => {
      mockHttp.fetch.mockResolvedValue(mockFetchResponse({}, 500, false));

      const { result } = renderHook(() => useLeafletFeedbackListQuery(), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toEqual(
        new Error('Failed to fetch leaflet feedback list: 500')
      );
    });
  });
});
