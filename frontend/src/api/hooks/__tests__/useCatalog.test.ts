import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useCatalogQuery, ProductType, GetCatalogListResponse } from '../useCatalog';
import { getAuthenticatedApiClient } from '../../client';

// Mock the API client
jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

// Mock data
const mockApiResponse: GetCatalogListResponse = {
  items: [
    {
      productCode: 'PROD001',
      productName: 'Test Product 1',
      type: ProductType.Product,
      stock: { eshop: 10, erp: 15, transport: 2, reserve: 3, available: 12 },
      properties: { optimalStockDaysSetup: 30, stockMinSetup: 5, batchSize: 10, seasonMonths: [] },
      location: 'Warehouse A',
      minimalOrderQuantity: '5',
      minimalManufactureQuantity: 10
    },
    {
      productCode: 'MAT002',
      productName: 'Test Material 2',
      type: ProductType.Material,
      stock: { eshop: 0, erp: 25, transport: 0, reserve: 5, available: 20 },
      properties: { optimalStockDaysSetup: 60, stockMinSetup: 10, batchSize: 20, seasonMonths: [3, 4, 5] },
      location: 'Warehouse B',
      minimalOrderQuantity: '10',
      minimalManufactureQuantity: 20
    },
    {
      productCode: 'SEMI003',
      productName: 'Test Semi-Product 3',
      type: ProductType.SemiProduct,
      stock: { eshop: 5, erp: 8, transport: 1, reserve: 2, available: 6 },
      properties: { optimalStockDaysSetup: 45, stockMinSetup: 3, batchSize: 5, seasonMonths: [] },
      location: 'Warehouse A',
      minimalOrderQuantity: '3',
      minimalManufactureQuantity: 5
    }
  ],
  totalCount: 3,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 1
};

describe('useCatalogQuery', () => {
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
      baseUrl: 'http://localhost:5001',
      getAuthHeaders: jest.fn().mockResolvedValue({
        'Content-Type': 'application/json',
        'Authorization': 'Bearer mock-token'
      })
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);

    // Mock fetch globally
    global.fetch = jest.fn();
  });

  afterEach(() => {
    jest.clearAllMocks();
    queryClient.clear();
  });

  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );

  describe('Basic Functionality', () => {
    test('should fetch catalog data successfully', async () => {
      (global.fetch as jest.Mock).mockResolvedValueOnce({
        ok: true,
        json: jest.fn().mockResolvedValue(mockApiResponse)
      });

      const { result } = renderHook(() => useCatalogQuery(), { wrapper });

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(result.current.data?.items).toHaveLength(3);
      expect(result.current.data?.totalCount).toBe(3);
      expect(result.current.error).toBeNull();
    });

    test('should handle API errors', async () => {
      (global.fetch as jest.Mock).mockResolvedValueOnce({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error'
      });

      const { result } = renderHook(() => useCatalogQuery(), { wrapper });

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(result.current.error).toBeTruthy();
      expect(result.current.data).toBeUndefined();
    });
  });

  describe('Filtering', () => {
    beforeEach(() => {
      (global.fetch as jest.Mock).mockResolvedValue({
        ok: true,
        json: jest.fn().mockResolvedValue(mockApiResponse)
      });
    });

    test('should pass product name filter to API', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('Test Product', '', '', 1, 20), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should pass productName parameter to API
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('productName=Test%20Product'),
        expect.any(Object)
      );
      
      expect(result.current.data?.items).toHaveLength(3); // Server returns filtered results
      expect(result.current.data?.totalCount).toBe(3);
    });

    test('should pass product code filter to API', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('', 'MAT', '', 1, 20), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should pass productCode parameter to API
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('productCode=MAT'),
        expect.any(Object)
      );
      
      expect(result.current.data?.items).toHaveLength(3); // Server returns filtered results
      expect(result.current.data?.totalCount).toBe(3);
    });

    test('should filter by product type', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('', '', ProductType.Material, 1, 20), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // This should be filtered on the server side (not client side)
      // So we expect the API to be called with the type parameter
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining(`type=${ProductType.Material}`),
        expect.any(Object)
      );
    });

    test('should combine multiple filters in API call', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('Test', 'PROD', '', 1, 20), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should pass both filters to API
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('productName=Test'),
        expect.any(Object)
      );
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('productCode=PROD'),
        expect.any(Object)
      );
      
      expect(result.current.data?.items).toHaveLength(3); // Server returns filtered results
    });
  });

  describe('Sorting', () => {
    beforeEach(() => {
      (global.fetch as jest.Mock).mockResolvedValue({
        ok: true,
        json: jest.fn().mockResolvedValue(mockApiResponse)
      });
    });

    test('should pass sort parameters to API', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('', '', '', 1, 20, 'productCode', false), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should pass sort parameters to API
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('sortBy=productCode'),
        expect.any(Object)
      );
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('sortDescending=false'),
        expect.any(Object)
      );

      expect(result.current.data?.items).toHaveLength(3);
    });

    test('should pass descending sort to API', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('', '', '', 1, 20, 'productCode', true), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('sortDescending=true'),
        expect.any(Object)
      );

      expect(result.current.data?.items).toHaveLength(3);
    });
  });

  describe('Pagination', () => {
    beforeEach(() => {
      (global.fetch as jest.Mock).mockResolvedValue({
        ok: true,
        json: jest.fn().mockResolvedValue(mockApiResponse)
      });
    });

    test('should pass pagination parameters to API', async () => {
      renderHook(() => 
        useCatalogQuery('', '', '', 2, 10), 
        { wrapper }
      );

      await waitFor(() => {
        expect(global.fetch).toHaveBeenCalledWith(
          expect.stringContaining('pageNumber=2'),
          expect.any(Object)
        );
        expect(global.fetch).toHaveBeenCalledWith(
          expect.stringContaining('pageSize=10'),
          expect.any(Object)
        );
      });
    });

    test('should pass sorting parameters to API', async () => {
      renderHook(() => 
        useCatalogQuery('', '', '', 1, 20, 'productName', true), 
        { wrapper }
      );

      await waitFor(() => {
        expect(global.fetch).toHaveBeenCalledWith(
          expect.stringContaining('sortBy=productName'),
          expect.any(Object)
        );
        expect(global.fetch).toHaveBeenCalledWith(
          expect.stringContaining('sortDescending=true'),
          expect.any(Object)
        );
      });
    });
  });

  describe('Server Response Handling', () => {
    beforeEach(() => {
      (global.fetch as jest.Mock).mockResolvedValue({
        ok: true,
        json: jest.fn().mockResolvedValue(mockApiResponse)
      });
    });

    test('should return server response as-is', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('Test Product', '', '', 1, 20), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should return exactly what server provides
      expect(result.current.data?.items).toHaveLength(3);
      expect(result.current.data?.totalCount).toBe(3);
      expect(result.current.data?.pageNumber).toBe(1);
      expect(result.current.data?.pageSize).toBe(20);
      expect(result.current.data?.totalPages).toBe(1);
    });

    test('should handle API filtering parameters correctly', async () => {
      const { result } = renderHook(() => 
        useCatalogQuery('NonExistentProduct', '', '', 1, 20), 
        { wrapper }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining('productName=NonExistentProduct'),
        expect.any(Object)
      );

      // Server response should be returned as-is
      expect(result.current.data?.items).toHaveLength(3);
      expect(result.current.data?.totalCount).toBe(3);
    });
  });
});