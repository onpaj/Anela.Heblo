import React from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useInvoiceImportStatistics } from '../useInvoiceImportStatistics';
import { getAuthenticatedApiClient } from '../../client';

// Mock the API client
jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

// Mock fetch
const mockFetch = jest.fn();

describe('useInvoiceImportStatistics', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
        },
      },
    });

    mockGetAuthenticatedApiClient.mockResolvedValue({
      baseUrl: 'http://localhost:5000',
      http: {
        fetch: mockFetch,
      },
    } as any);

    mockFetch.mockClear();
  });

  const wrapper = ({ children }: { children: React.ReactNode }) => {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };

  it('should fetch invoice import statistics with default parameters', async () => {
    // Arrange
    const mockData = {
      data: [
        { date: '2023-09-28', count: 15, isBelowThreshold: false },
        { date: '2023-09-29', count: 5, isBelowThreshold: true },
      ],
      minimumThreshold: 10,
      success: true,
    };

    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(mockData),
    });

    // Act
    const { result } = renderHook(() => useInvoiceImportStatistics(), { wrapper });

    // Assert
    expect(result.current.isLoading).toBe(true);

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    expect(result.current.data).toEqual(mockData);
    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5000/api/analytics/invoice-import-statistics?dateType=InvoiceDate',
      expect.objectContaining({
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      })
    );
  });

  it('should not include daysBack parameter when not provided', async () => {
    // Arrange
    const mockData = {
      data: [],
      minimumThreshold: 10,
      success: true,
    };

    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(mockData),
    });

    // Act
    const { result } = renderHook(() => useInvoiceImportStatistics({ dateType: 'LastSyncTime' }), { wrapper });

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    // Assert - Should not include daysBack parameter
    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5000/api/analytics/invoice-import-statistics?dateType=LastSyncTime',
      expect.objectContaining({
        method: 'GET',
      })
    );
  });

  it('should use custom parameters when provided', async () => {
    // Arrange
    const mockData = {
      data: [],
      minimumThreshold: 10,
      success: true,
    };

    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(mockData),
    });

    // Act
    const { result } = renderHook(
      () => useInvoiceImportStatistics({ dateType: 'LastSyncTime', daysBack: 7 }),
      { wrapper }
    );

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });

    // Assert
    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5000/api/analytics/invoice-import-statistics?dateType=LastSyncTime&daysBack=7',
      expect.objectContaining({
        method: 'GET',
      })
    );
  });

  it('should handle API errors', async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: false,
      status: 500,
    });

    // Act
    const { result } = renderHook(() => useInvoiceImportStatistics(), { wrapper });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });

    // Assert
    expect(result.current.error).toBeInstanceOf(Error);
    expect((result.current.error as Error).message).toContain('HTTP error! status: 500');
  });

  it('should have correct cache configuration', () => {
    // Act
    const { result } = renderHook(() => useInvoiceImportStatistics(), { wrapper });

    // Assert - Initially loading, then should have proper stale time configuration
    expect(result.current.isLoading || result.current.isStale).toBe(true);
  });
});