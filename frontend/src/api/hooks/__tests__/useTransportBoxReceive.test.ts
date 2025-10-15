import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useTransportBoxReceive } from '../useTransportBoxReceive';
import { getAuthenticatedApiClient } from '../../client';

// Mock the API client
jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    transportBox: ['transportBox']
  }
}));

const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: {
    fetch: jest.fn()
  }
};

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      {children}
    </QueryClientProvider>
  );

  return Wrapper;
};

describe('useTransportBoxReceive', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
  });

  describe('getByCode', () => {
    it('successfully fetches box by code', async () => {
      const mockResponse = {
        transportBox: {
          id: 1,
          code: 'B001',
          state: 'InTransit',
          items: []
        },
        success: true
      };

      (mockApiClient.http.fetch as jest.Mock).mockResolvedValue({
        ok: true,
        json: jest.fn().resolvedValue(mockResponse)
      });

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      const response = await result.current.getByCode('B001');

      expect(mockApiClient.http.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/transport-boxes/by-code/B001',
        {
          method: 'GET',
          headers: {
            'Content-Type': 'application/json',
          },
        }
      );

      expect(response).toEqual(mockResponse);
    });

    it('handles 400 error response', async () => {
      const mockErrorResponse = {
        errorMessage: 'Box je ve stavu Otevřený a nelze jej přijmout'
      };

      (mockApiClient.http.fetch as jest.Mock).mockResolvedValue({
        ok: false,
        status: 400,
        json: jest.fn().resolvedValue(mockErrorResponse)
      });

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      const response = await result.current.getByCode('B001');

      expect(response).toEqual({
        success: false,
        errorMessage: 'Box je ve stavu Otevřený a nelze jej přijmout'
      });
    });

    it('handles non-400 HTTP errors', async () => {
      (mockApiClient.http.fetch as jest.Mock).mockResolvedValue({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
        json: jest.fn().resolvedValue({})
      });

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      await expect(result.current.getByCode('B001')).rejects.toThrow('HTTP 500: Internal Server Error');
    });

    it('properly encodes box code in URL', async () => {
      (mockApiClient.http.fetch as jest.Mock).mockResolvedValue({
        ok: true,
        json: jest.fn().resolvedValue({ success: true })
      });

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      await result.current.getByCode('B/001');

      expect(mockApiClient.http.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/transport-boxes/by-code/B%2F001',
        expect.any(Object)
      );
    });
  });

  describe('receive', () => {
    it('successfully receives transport box', async () => {
      const mockResponse = {
        success: true,
        boxId: 1,
        boxCode: 'B001'
      };

      (mockApiClient.http.fetch as jest.Mock).mockResolvedValue({
        ok: true,
        json: jest.fn().resolvedValue(mockResponse)
      });

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      const response = await result.current.receive(1, 'TestUser');

      expect(mockApiClient.http.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/transport-boxes/1/receive',
        {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            boxId: 1,
            userName: 'TestUser'
          })
        }
      );

      expect(response).toEqual(mockResponse);
    });

    it('handles 400 error during receive', async () => {
      const mockErrorResponse = {
        errorMessage: 'Box je ve stavu Přijatý a nelze jej přijmout'
      };

      (mockApiClient.http.fetch as jest.Mock).mockResolvedValue({
        ok: false,
        status: 400,
        json: jest.fn().resolvedValue(mockErrorResponse)
      });

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      const response = await result.current.receive(1, 'TestUser');

      expect(response).toEqual({
        success: false,
        errorMessage: 'Box je ve stavu Přijatý a nelze jej přijmout',
        boxId: 1
      });
    });

    it('handles non-400 HTTP errors during receive', async () => {
      (mockApiClient.http.fetch as jest.Mock).mockResolvedValue({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
        json: jest.fn().resolvedValue({})
      });

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      await expect(result.current.receive(1, 'TestUser')).rejects.toThrow('HTTP 500: Internal Server Error');
    });

    it('sets isReceiving to true during mutation', async () => {
      (mockApiClient.http.fetch as jest.Mock).mockImplementation(
        () => new Promise((resolve) => 
          setTimeout(() => resolve({ ok: true, json: () => Promise.resolve({ success: true }) }), 100)
        )
      );

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      expect(result.current.isReceiving).toBe(false);

      const receivePromise = result.current.receive(1, 'TestUser');

      await waitFor(() => {
        expect(result.current.isReceiving).toBe(true);
      });

      await receivePromise;

      await waitFor(() => {
        expect(result.current.isReceiving).toBe(false);
      });
    });
  });
});