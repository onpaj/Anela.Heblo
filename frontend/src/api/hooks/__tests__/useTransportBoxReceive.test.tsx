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

const createMockApiClient = () => ({
  transportBox_GetTransportBoxByCode: jest.fn(),
  transportBox_ReceiveTransportBox: jest.fn()
});

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
  let mockApiClient: ReturnType<typeof createMockApiClient>;

  beforeEach(() => {
    jest.clearAllMocks();
    mockApiClient = createMockApiClient();
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

      mockApiClient.transportBox_GetTransportBoxByCode.mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      const response = await result.current.getByCode('B001');

      expect(mockApiClient.transportBox_GetTransportBoxByCode).toHaveBeenCalledWith('B001');
      expect(response).toEqual(mockResponse);
    });

    it('handles errors when fetching box by code', async () => {
      const mockError = new Error('Box not found');
      mockApiClient.transportBox_GetTransportBoxByCode.mockRejectedValue(mockError);

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      await expect(result.current.getByCode('B001')).rejects.toThrow('Box not found');
    });
  });

  describe('receive', () => {
    it('successfully receives transport box', async () => {
      const mockResponse = {
        success: true,
        boxId: 1,
        boxCode: 'B001'
      };

      mockApiClient.transportBox_ReceiveTransportBox.mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      const response = await result.current.receive(1, 'TestUser');

      expect(mockApiClient.transportBox_ReceiveTransportBox).toHaveBeenCalledWith(
        1,
        expect.objectContaining({
          boxId: 1,
          userName: 'TestUser'
        })
      );

      expect(response).toEqual(mockResponse);
    });

    it('handles errors during receive', async () => {
      const mockError = new Error('Box already received');
      mockApiClient.transportBox_ReceiveTransportBox.mockRejectedValue(mockError);

      const { result } = renderHook(() => useTransportBoxReceive(), {
        wrapper: createWrapper()
      });

      await expect(result.current.receive(1, 'TestUser')).rejects.toThrow('Box already received');
    });

    it('sets isReceiving to true during mutation', async () => {
      mockApiClient.transportBox_ReceiveTransportBox.mockImplementation(
        () => new Promise((resolve) =>
          setTimeout(() => resolve({ success: true, boxId: 1, boxCode: 'B001' }), 100)
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
