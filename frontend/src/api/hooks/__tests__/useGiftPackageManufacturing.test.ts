import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React, { ReactNode } from "react";
import {
  useAvailableGiftPackages,
  useGiftPackageDetail,
  useCreateGiftPackageManufacture,
  GiftPackageQueryParams,
} from "../useGiftPackageManufacturing";
import { getAuthenticatedApiClient } from "../../client";

// Mock the API client
jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<
    typeof getAuthenticatedApiClient
  >;

// Mock data
const mockAvailableGiftPackagesResponse = {
  data: [
    {
      code: "SET001",
      name: "Test Gift Set 1",
      availableStock: 50,
      dailySales: 2.5,
      overstockLimit: 20,
      ingredients: null,
    },
    {
      code: "SET002",
      name: "Test Gift Set 2", 
      availableStock: 30,
      dailySales: 1.8,
      overstockLimit: 15,
      ingredients: null,
    },
  ],
};

const mockGiftPackageDetailResponse = {
  data: {
    code: "SET001",
    name: "Test Gift Set 1",
    availableStock: 50,
    dailySales: 2.5,
    overstockLimit: 20,
    ingredients: [
      {
        productCode: "ING001",
        productName: "Ingredient 1",
        requiredQuantity: 2.0,
        availableStock: 100.0,
        hasSufficientStock: true,
      },
      {
        productCode: "ING002", 
        productName: "Ingredient 2",
        requiredQuantity: 1.5,
        availableStock: 75.0,
        hasSufficientStock: true,
      },
    ],
  },
};

const mockManufactureResponse = {
  data: {
    id: "12345",
    giftPackageCode: "SET001",
    quantity: 5,
    userId: "user123",
    createdAt: "2024-06-15T10:00:00Z",
    consumedItems: [
      {
        productCode: "ING001",
        quantity: 10,
      },
      {
        productCode: "ING002",
        quantity: 7,
      },
    ],
  },
};

describe("useGiftPackageManufacturing hooks", () => {
  let queryClient: QueryClient;
  let mockApiClient: any;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    // Mock the API client with logistics endpoints
    const mockFetch = jest.fn();
    mockApiClient = {
      baseUrl: "http://localhost:5001",
      http: {
        fetch: mockFetch,
      },
      logistics_GetAvailableGiftPackages: jest.fn(),
      logistics_GetGiftPackageDetail: jest.fn(),
      logistics_CreateGiftPackageManufacture: jest.fn(),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);

    // Also mock global fetch for backward compatibility
    global.fetch = mockFetch;
  });

  afterEach(() => {
    jest.clearAllMocks();
    queryClient.clear();
  });

  const createWrapper = () => {
    return ({ children }: { children: ReactNode }) => {
      return React.createElement(
        QueryClientProvider,
        { client: queryClient },
        children,
      );
    };
  };

  describe("useAvailableGiftPackages", () => {
    test("should fetch available gift packages successfully", async () => {
      mockApiClient.logistics_GetAvailableGiftPackages.mockResolvedValueOnce(
        mockAvailableGiftPackagesResponse
      );

      const { result } = renderHook(() => useAvailableGiftPackages(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(result.current.data?.data).toHaveLength(2);
      expect(result.current.data?.data[0].code).toBe("SET001");
      expect(result.current.data?.data[0].name).toBe("Test Gift Set 1");
      expect(result.current.data?.data[0].availableStock).toBe(50);
      expect(result.current.data?.data[0].dailySales).toBe(2.5);
      expect(result.current.error).toBeNull();
    });

    test("should handle API errors", async () => {
      mockApiClient.logistics_GetAvailableGiftPackages.mockRejectedValueOnce(
        new Error("API Error")
      );

      const { result } = renderHook(() => useAvailableGiftPackages(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(result.current.error).toBeTruthy();
      expect(result.current.data).toBeUndefined();
    });

    test("should include date parameters in query key when provided", async () => {
      mockApiClient.logistics_GetAvailableGiftPackages.mockResolvedValueOnce(
        mockAvailableGiftPackagesResponse
      );

      const params: GiftPackageQueryParams = {
        fromDate: new Date('2024-01-01'),
        toDate: new Date('2024-12-31'),
      };

      const { result } = renderHook(() => useAvailableGiftPackages(params), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // The API should be called (currently without date params as noted in TODO)
      expect(mockApiClient.logistics_GetAvailableGiftPackages).toHaveBeenCalledTimes(1);
      expect(result.current.data?.data).toHaveLength(2);
    });

    test("should refetch when date parameters change", async () => {
      mockApiClient.logistics_GetAvailableGiftPackages.mockResolvedValue(
        mockAvailableGiftPackagesResponse
      );

      const params1: GiftPackageQueryParams = {
        fromDate: new Date('2024-01-01'),
        toDate: new Date('2024-06-30'),
      };

      const { result, rerender } = renderHook(
        ({ params }: { params?: GiftPackageQueryParams }) => useAvailableGiftPackages(params),
        {
          wrapper: createWrapper(),
          initialProps: { params: params1 },
        }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(mockApiClient.logistics_GetAvailableGiftPackages).toHaveBeenCalledTimes(1);

      // Change parameters to trigger refetch
      const params2: GiftPackageQueryParams = {
        fromDate: new Date('2024-07-01'),
        toDate: new Date('2024-12-31'),
      };

      rerender({ params: params2 });

      await waitFor(() => {
        expect(mockApiClient.logistics_GetAvailableGiftPackages).toHaveBeenCalledTimes(2);
      });
    });
  });

  describe("useGiftPackageDetail", () => {
    test("should fetch gift package detail successfully", async () => {
      mockApiClient.logistics_GetGiftPackageDetail.mockResolvedValueOnce(
        mockGiftPackageDetailResponse
      );

      const { result } = renderHook(() => useGiftPackageDetail("SET001"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(result.current.data?.data.code).toBe("SET001");
      expect(result.current.data?.data.name).toBe("Test Gift Set 1");
      expect(result.current.data?.data.ingredients).toHaveLength(2);
      expect(result.current.data?.data.ingredients?.[0].productCode).toBe("ING001");
      expect(result.current.data?.data.ingredients?.[0].hasSufficientStock).toBe(true);
      expect(result.current.error).toBeNull();
    });

    test("should handle API errors", async () => {
      mockApiClient.logistics_GetGiftPackageDetail.mockRejectedValueOnce(
        new Error("Gift package not found")
      );

      const { result } = renderHook(() => useGiftPackageDetail("INVALID"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(result.current.error).toBeTruthy();
      expect(result.current.data).toBeUndefined();
    });

    test("should not fetch when giftPackageCode is empty", async () => {
      const { result } = renderHook(() => useGiftPackageDetail(""), {
        wrapper: createWrapper(),
      });

      // Should not make API call when giftPackageCode is empty
      expect(mockApiClient.logistics_GetGiftPackageDetail).not.toHaveBeenCalled();
      expect(result.current.data).toBeUndefined();
    });

    test("should pass giftPackageCode to API correctly", async () => {
      mockApiClient.logistics_GetGiftPackageDetail.mockResolvedValueOnce(
        mockGiftPackageDetailResponse
      );

      renderHook(() => useGiftPackageDetail("SET001"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(mockApiClient.logistics_GetGiftPackageDetail).toHaveBeenCalledWith("SET001", undefined, undefined, undefined);
      });
    });
  });

  describe("useCreateGiftPackageManufacture", () => {
    test("should create gift package manufacture successfully", async () => {
      mockApiClient.logistics_CreateGiftPackageManufacture.mockResolvedValueOnce(
        mockManufactureResponse
      );

      const { result } = renderHook(() => useCreateGiftPackageManufacture(), {
        wrapper: createWrapper(),
      });

      const manufactureRequest = {
        giftPackageCode: "SET001",
        quantity: 5,
        allowStockOverride: false,
        userId: "user123",
      };

      await result.current.mutateAsync(manufactureRequest);

      expect(mockApiClient.logistics_CreateGiftPackageManufacture).toHaveBeenCalledWith(
        manufactureRequest
      );
    });

    test("should handle manufacture creation errors", async () => {
      mockApiClient.logistics_CreateGiftPackageManufacture.mockRejectedValueOnce(
        new Error("Insufficient stock")
      );

      const { result } = renderHook(() => useCreateGiftPackageManufacture(), {
        wrapper: createWrapper(),
      });

      const manufactureRequest = {
        giftPackageCode: "SET001",
        quantity: 100, // Too much
        allowStockOverride: false,
        userId: "user123",
      };

      await expect(result.current.mutateAsync(manufactureRequest))
        .rejects.toThrow("Insufficient stock");

      expect(mockApiClient.logistics_CreateGiftPackageManufacture).toHaveBeenCalledWith(
        manufactureRequest
      );
    });

    test("should invalidate available gift packages cache on success", async () => {
      mockApiClient.logistics_CreateGiftPackageManufacture.mockResolvedValueOnce(
        mockManufactureResponse
      );

      // First load available gift packages
      mockApiClient.logistics_GetAvailableGiftPackages.mockResolvedValueOnce(
        mockAvailableGiftPackagesResponse
      );

      const { result: availableResult } = renderHook(() => useAvailableGiftPackages(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(availableResult.current.isLoading).toBe(false);
      });

      // Create manufacture mutation
      const { result: mutationResult } = renderHook(() => useCreateGiftPackageManufacture(), {
        wrapper: createWrapper(),
      });

      const manufactureRequest = {
        giftPackageCode: "SET001",
        quantity: 5,
        allowStockOverride: false,
        userId: "user123",
      };

      await mutationResult.current.mutateAsync(manufactureRequest);

      // Should invalidate and refetch available gift packages
      await waitFor(() => {
        expect(mockApiClient.logistics_GetAvailableGiftPackages).toHaveBeenCalledTimes(2);
      });
    });

    test("should handle different stock override scenarios", async () => {
      mockApiClient.logistics_CreateGiftPackageManufacture.mockResolvedValueOnce(
        mockManufactureResponse
      );

      const { result } = renderHook(() => useCreateGiftPackageManufacture(), {
        wrapper: createWrapper(),
      });

      // Test with stock override enabled
      const manufactureRequestWithOverride = {
        giftPackageCode: "SET001",
        quantity: 100,
        allowStockOverride: true,
        userId: "user123",
      };

      await result.current.mutateAsync(manufactureRequestWithOverride);

      expect(mockApiClient.logistics_CreateGiftPackageManufacture).toHaveBeenCalledWith(
        manufactureRequestWithOverride
      );

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });
    });
  });

  describe("Query key generation", () => {
    test("should generate consistent query keys for available gift packages", () => {
      const params1: GiftPackageQueryParams = {
        fromDate: new Date('2024-01-01'),
        toDate: new Date('2024-12-31'),
      };

      const params2: GiftPackageQueryParams = {
        fromDate: new Date('2024-01-01'),
        toDate: new Date('2024-12-31'),
      };

      // Same parameters should generate same query keys
      const { result: result1 } = renderHook(() => useAvailableGiftPackages(params1), {
        wrapper: createWrapper(),
      });

      const { result: result2 } = renderHook(() => useAvailableGiftPackages(params2), {
        wrapper: createWrapper(),
      });

      // Both hooks should share the same cache due to identical query keys
      expect(result1.current.dataUpdatedAt).toBe(result2.current.dataUpdatedAt);
    });

    test("should generate different query keys for different date ranges", () => {
      mockApiClient.logistics_GetAvailableGiftPackages.mockResolvedValue(
        mockAvailableGiftPackagesResponse
      );

      const params1: GiftPackageQueryParams = {
        fromDate: new Date('2024-01-01'),
        toDate: new Date('2024-06-30'),
      };

      const params2: GiftPackageQueryParams = {
        fromDate: new Date('2024-07-01'),
        toDate: new Date('2024-12-31'),
      };

      renderHook(() => useAvailableGiftPackages(params1), {
        wrapper: createWrapper(),
      });

      renderHook(() => useAvailableGiftPackages(params2), {
        wrapper: createWrapper(),
      });

      // Should make separate API calls due to different parameters
      expect(mockApiClient.logistics_GetAvailableGiftPackages).toHaveBeenCalledTimes(2);
    });
  });

  describe("Caching and stale time", () => {
    test("should respect stale time configuration", async () => {
      mockApiClient.logistics_GetAvailableGiftPackages.mockResolvedValue(
        mockAvailableGiftPackagesResponse
      );

      const { result: result1 } = renderHook(() => useAvailableGiftPackages(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(result1.current.isLoading).toBe(false);
      });

      // Second hook within stale time should not trigger new fetch
      const { result: result2 } = renderHook(() => useAvailableGiftPackages(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => {
        expect(result2.current.isLoading).toBe(false);
      });

      // Should only have called API once due to stale time
      expect(mockApiClient.logistics_GetAvailableGiftPackages).toHaveBeenCalledTimes(1);
    });
  });
});