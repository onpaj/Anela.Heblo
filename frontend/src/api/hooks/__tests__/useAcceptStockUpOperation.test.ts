import React, { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAcceptStockUpOperationMutation } from '../useStockUpOperations';
import { getAuthenticatedApiClient } from '../../client';
import { AcceptStockUpOperationResponse, StockUpResultStatus } from '../../generated/api-client';

// Mock the API client module but preserve QUERY_KEYS
jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

describe('useAcceptStockUpOperationMutation', () => {
  let queryClient: QueryClient;
  let mockApiClient: any;

  beforeEach(() => {
    // Create a fresh QueryClient for each test
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    // Clear all mocks
    jest.clearAllMocks();
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it('should successfully accept a stock operation', async () => {
    // Arrange
    const mockResponse = new AcceptStockUpOperationResponse({
      status: StockUpResultStatus.Completed,
      errorMessage: undefined,
      success: true,
      errors: [],
    });

    mockApiClient = {
      stockUpOperations_AcceptOperation: jest.fn().mockResolvedValue(mockResponse),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    // Act
    const { result } = renderHook(() => useAcceptStockUpOperationMutation(), { wrapper });

    result.current.mutate(123);

    // Assert
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockApiClient.stockUpOperations_AcceptOperation).toHaveBeenCalledWith(123);
    expect(result.current.data).toEqual(mockResponse);
  });

  it('should handle error when accepting stock operation fails', async () => {
    // Arrange
    const mockError = new Error('Failed to accept operation');

    mockApiClient = {
      stockUpOperations_AcceptOperation: jest.fn().mockRejectedValue(mockError),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    // Act
    const { result } = renderHook(() => useAcceptStockUpOperationMutation(), { wrapper });

    result.current.mutate(123);

    // Assert
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toEqual(mockError);
  });

  it('should invalidate stock operations queries on success', async () => {
    // Arrange
    const mockResponse = new AcceptStockUpOperationResponse({
      status: StockUpResultStatus.Completed,
      errorMessage: undefined,
      success: true,
      errors: [],
    });

    mockApiClient = {
      stockUpOperations_AcceptOperation: jest.fn().mockResolvedValue(mockResponse),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    // Spy on queryClient invalidation
    const invalidateQueriesSpy = jest.spyOn(queryClient, 'invalidateQueries');

    // Act
    const { result } = renderHook(() => useAcceptStockUpOperationMutation(), { wrapper });

    result.current.mutate(123);

    // Assert
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Verify both list and summaries were invalidated
    // Check that invalidateQueries was called with the expected query keys
    const calls = invalidateQueriesSpy.mock.calls;
    expect(calls.length).toBeGreaterThanOrEqual(2);

    const queryKeys = calls.map(call => call[0].queryKey);
    expect(queryKeys).toEqual(
      expect.arrayContaining([
        ['stock-up-operations', 'list'],
        ['stock-up-operations', 'summary'],
      ])
    );
  });
});
