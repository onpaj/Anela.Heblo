import React from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useStockUpOperationsSummary } from '../useStockUpOperations';
import { getAuthenticatedApiClient } from '../../client';
import { StockUpSourceType, GetStockUpOperationsSummaryResponse } from '../../generated/api-client';

// Mock the API client module but preserve QUERY_KEYS
jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));

describe('useStockUpOperationsSummary', () => {
  let queryClient;
  let mockApiClient;
  let mockGetAuthenticatedApiClient;

  beforeEach(() => {
    // Create a fresh QueryClient for each test
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });

    // Clear all mocks
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient = getAuthenticatedApiClient;
  });

  const wrapper = ({ children }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it('does NOT call the API when enabled is false', async () => {
    // Arrange
    const mockResponse = new GetStockUpOperationsSummaryResponse({
      pendingCount: 0,
      submittedCount: 0,
      failedCount: 0,
    });

    mockApiClient = {
      stockUpOperations_GetSummary: jest.fn().mockResolvedValue(mockResponse),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);

    // Act
    renderHook(
      () =>
        useStockUpOperationsSummary(StockUpSourceType.TransportBox, {
          enabled: false,
        }),
      { wrapper }
    );

    // Wait a bit to ensure the hook doesn't fetch
    await new Promise((r) => setTimeout(r, 50));

    // Assert
    expect(mockApiClient.stockUpOperations_GetSummary).not.toHaveBeenCalled();
  });

  it('calls the API when enabled defaults to true', async () => {
    // Arrange
    const mockResponse = new GetStockUpOperationsSummaryResponse({
      pendingCount: 0,
      submittedCount: 0,
      failedCount: 0,
    });

    mockApiClient = {
      stockUpOperations_GetSummary: jest.fn().mockResolvedValue(mockResponse),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);

    // Act
    const { result } = renderHook(
      () => useStockUpOperationsSummary(StockUpSourceType.TransportBox),
      { wrapper }
    );

    // Assert
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockApiClient.stockUpOperations_GetSummary).toHaveBeenCalledTimes(1);
  });
});
