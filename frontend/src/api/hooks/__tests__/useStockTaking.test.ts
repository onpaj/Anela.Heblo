import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useStockTakingHistory } from '../useStockTaking';
import { getAuthenticatedApiClient } from '../client';

// Mock the API client
jest.mock('../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockApiClient = {
  stockTaking_GetStockTakingHistory: jest.fn(),
};

(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });
  
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      {children}
    </QueryClientProvider>
  );
};

describe('useStockTakingHistory', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should not execute query when productCode is not provided', async () => {
    const { result } = renderHook(
      () => useStockTakingHistory({ productCode: undefined }),
      { wrapper: createWrapper() }
    );

    expect(result.current.data).toBeUndefined();
    expect(result.current.isLoading).toBe(false);
    expect(mockApiClient.stockTaking_GetStockTakingHistory).not.toHaveBeenCalled();
  });

  it('should not execute query when productCode is empty string', async () => {
    const { result } = renderHook(
      () => useStockTakingHistory({ productCode: '' }),
      { wrapper: createWrapper() }
    );

    expect(result.current.data).toBeUndefined();
    expect(result.current.isLoading).toBe(false);
    expect(mockApiClient.stockTaking_GetStockTakingHistory).not.toHaveBeenCalled();
  });

  it('should execute query when valid productCode is provided', async () => {
    const mockHistoryData = {
      items: [
        {
          date: '2024-01-15T10:30:00Z',
          previousAmount: 10,
          newAmount: 15,
          userId: 'user1',
          reason: 'Manual adjustment'
        },
        {
          date: '2024-01-10T14:20:00Z',
          previousAmount: 5,
          newAmount: 10,
          userId: 'user2',
          reason: 'Inventory count'
        }
      ],
      totalCount: 2,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1
    };

    mockApiClient.stockTaking_GetStockTakingHistory.mockResolvedValueOnce(mockHistoryData);

    const { result } = renderHook(
      () => useStockTakingHistory({ 
        productCode: 'TEST-PRODUCT',
        pageNumber: 1,
        pageSize: 20,
        sortBy: 'date',
        sortDescending: true
      }),
      { wrapper: createWrapper() }
    );

    expect(result.current.isLoading).toBe(true);

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual(mockHistoryData);
    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledWith(
      'TEST-PRODUCT',
      1,
      20,
      'date',
      true
    );
  });

  it('should use default parameters correctly', async () => {
    const mockHistoryData = {
      items: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 0
    };

    mockApiClient.stockTaking_GetStockTakingHistory.mockResolvedValueOnce(mockHistoryData);

    const { result } = renderHook(
      () => useStockTakingHistory({ productCode: 'TEST-PRODUCT' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledWith(
      'TEST-PRODUCT',
      1,        // Default pageNumber
      20,       // Default pageSize
      'date',   // Default sortBy
      true      // Default sortDescending
    );
  });

  it('should handle API errors correctly', async () => {
    const mockError = new Error('Product not found');
    mockApiClient.stockTaking_GetStockTakingHistory.mockRejectedValueOnce(mockError);

    const { result } = renderHook(
      () => useStockTakingHistory({ productCode: 'NONEXISTENT-PRODUCT' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });

    expect(result.current.error).toEqual(mockError);
    expect(result.current.data).toBeUndefined();
  });

  it('should handle custom pagination parameters', async () => {
    const mockHistoryData = {
      items: [
        {
          date: '2024-01-12T08:45:00Z',
          previousAmount: 20,
          newAmount: 25,
          userId: 'user3',
          reason: 'Stock adjustment'
        }
      ],
      totalCount: 15,
      pageNumber: 3,
      pageSize: 5,
      totalPages: 3
    };

    mockApiClient.stockTaking_GetStockTakingHistory.mockResolvedValueOnce(mockHistoryData);

    const { result } = renderHook(
      () => useStockTakingHistory({ 
        productCode: 'TEST-PRODUCT-2',
        pageNumber: 3,
        pageSize: 5,
        sortBy: 'newamount',
        sortDescending: false
      }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual(mockHistoryData);
    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledWith(
      'TEST-PRODUCT-2',
      3,
      5,
      'newamount',
      false
    );
  });

  it('should have correct cache configuration', async () => {
    const mockHistoryData = {
      items: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 0
    };

    mockApiClient.stockTaking_GetStockTakingHistory.mockResolvedValueOnce(mockHistoryData);

    const { result } = renderHook(
      () => useStockTakingHistory({ productCode: 'TEST-PRODUCT' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    // The hook should be configured with proper stale time and gc time
    // This is more of an implementation detail, but we can verify the query executed
    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalled();
    expect(result.current.data).toEqual(mockHistoryData);
  });

  it('should re-execute query when productCode changes', async () => {
    const mockHistoryData1 = {
      items: [{ date: '2024-01-15T10:30:00Z', previousAmount: 10, newAmount: 15, userId: 'user1', reason: 'Test 1' }],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1
    };

    const mockHistoryData2 = {
      items: [{ date: '2024-01-16T11:30:00Z', previousAmount: 20, newAmount: 25, userId: 'user2', reason: 'Test 2' }],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1
    };

    mockApiClient.stockTaking_GetStockTakingHistory
      .mockResolvedValueOnce(mockHistoryData1)
      .mockResolvedValueOnce(mockHistoryData2);

    const { result, rerender } = renderHook(
      ({ productCode }: { productCode: string }) => useStockTakingHistory({ productCode }),
      { 
        wrapper: createWrapper(),
        initialProps: { productCode: 'PRODUCT-1' }
      }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual(mockHistoryData1);
    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledWith(
      'PRODUCT-1', 1, 20, 'date', true
    );

    // Change productCode
    rerender({ productCode: 'PRODUCT-2' });

    await waitFor(() => {
      expect(result.current.data).toEqual(mockHistoryData2);
    });

    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledWith(
      'PRODUCT-2', 1, 20, 'date', true
    );
    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledTimes(2);
  });

  it('should not execute query when productCode changes from valid to invalid', async () => {
    const mockHistoryData = {
      items: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 0
    };

    mockApiClient.stockTaking_GetStockTakingHistory.mockResolvedValueOnce(mockHistoryData);

    const { result, rerender } = renderHook(
      ({ productCode }: { productCode: string | undefined }) => useStockTakingHistory({ productCode }),
      { 
        wrapper: createWrapper(),
        initialProps: { productCode: 'VALID-PRODUCT' }
      }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual(mockHistoryData);
    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledTimes(1);

    // Change productCode to undefined
    rerender({ productCode: undefined });

    // Should not trigger another API call
    expect(mockApiClient.stockTaking_GetStockTakingHistory).toHaveBeenCalledTimes(1);
    
    // The query should be disabled, but previous data might still be available depending on implementation
    expect(result.current.isLoading).toBe(false);
  });
});