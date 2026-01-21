import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React, { ReactNode } from "react";
import { useStockUpOperationsSummary } from "../useStockUpOperations";
import { getAuthenticatedApiClient } from "../../client";
import { StockUpSourceType } from "../../generated/api-client";

// Mock the API client
jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<
    typeof getAuthenticatedApiClient
  >;

const mockSummaryResponse = {
  success: true,
  pendingCount: 2,
  submittedCount: 1,
  failedCount: 1,
  totalInQueue: 3,
};

describe("useStockUpOperationsSummary", () => {
  let queryClient: QueryClient;
  let mockApiClient: any;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    mockApiClient = {
      stockUpOperations_GetSummary: jest.fn().mockResolvedValue(mockSummaryResponse),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it("should fetch summary with no source type filter", async () => {
    const { result } = renderHook(() => useStockUpOperationsSummary(), {
      wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockApiClient.stockUpOperations_GetSummary).toHaveBeenCalledWith(undefined);
    expect(result.current.data).toEqual(mockSummaryResponse);
  });

  it("should fetch summary filtered by GiftPackageManufacture", async () => {
    const { result } = renderHook(
      () => useStockUpOperationsSummary(StockUpSourceType.GiftPackageManufacture),
      { wrapper }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockApiClient.stockUpOperations_GetSummary).toHaveBeenCalledWith(
      StockUpSourceType.GiftPackageManufacture
    );
    expect(result.current.data).toEqual(mockSummaryResponse);
  });

  it("should handle API errors gracefully", async () => {
    mockApiClient.stockUpOperations_GetSummary.mockRejectedValue(
      new Error("API Error")
    );

    const { result } = renderHook(() => useStockUpOperationsSummary(), {
      wrapper,
    });

    await waitFor(
      () => expect(result.current.isError).toBe(true),
      { timeout: 3000 } // Allow time for retry logic
    );
    expect(result.current.error).toBeTruthy();
    expect((result.current.error as Error).message).toBe("API Error");
  });
});
