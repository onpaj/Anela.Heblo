import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useInventoryQuery } from '../useInventory';
import { getAuthenticatedApiClient } from '../../client';

// Mock the API client
jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

// Mock current date for consistent testing
const mockCurrentDate = new Date('2024-01-15T12:00:00Z');
jest.useFakeTimers();
jest.setSystemTime(mockCurrentDate);

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

describe('useInventory - Complex Sorting Logic', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  afterAll(() => {
    jest.useRealTimers();
  });

  const mockApiClient = {
    http: {
      fetch: jest.fn(),
    },
    baseUrl: 'http://localhost:5001',
    catalog_GetCatalogList: jest.fn(),
  };

  // Simple test to verify basic functionality
  it('should make API calls and return data', async () => {
    // Set up the mock
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
    
    const mockInventoryItems = [
      {
        productCode: 'TEST-1',
        productName: 'Test Product',
        location: 'A1',
        lastStockTaking: '2024-01-10T10:00:00Z'
      }
    ];

    // Mock three calls to catalog_GetCatalogList for Product, Goods, Set types
    mockApiClient.catalog_GetCatalogList
      .mockResolvedValueOnce({
        items: mockInventoryItems,
        totalCount: 1,
        pageNumber: 1,
        pageSize: 1000,
        totalPages: 1
      })
      .mockResolvedValueOnce({
        items: [], 
        totalCount: 0, 
        pageNumber: 1, 
        pageSize: 1000, 
        totalPages: 0 
      })
      .mockResolvedValueOnce({
        items: [], 
        totalCount: 0, 
        pageNumber: 1, 
        pageSize: 1000, 
        totalPages: 0 
      });

    const { result } = renderHook(
      () => useInventoryQuery('', '', '', 1, 20),
      { wrapper: createWrapper() }
    );

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    // Debug logging
    console.log('Test result:', {
      data: result.current.data,
      isLoading: result.current.isLoading,
      isError: result.current.isError,
      error: result.current.error
    });

    expect(result.current.data?.items).toBeDefined();
    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.items[0]?.productCode).toBe('TEST-1');
  });

  describe('LastInventoryDays Sorting - Descending (Default)', () => {
    it('should sort items without inventory first, then items with inventory by oldest first', async () => {
      // Set up the mock
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      
      // Mock response for each inventory type - since no specific type is provided,
      // the hook will make separate calls for Product, Goods, and Set types
      const mockInventoryItems = [
        {
          productCode: 'PRODUCT-1',
          productName: 'Product 1',
          location: 'A1',
          lastStockTaking: '2024-01-10T10:00:00Z' // 5 days ago
        },
        {
          productCode: 'PRODUCT-2',
          productName: 'Product 2',
          location: 'B1',
          lastStockTaking: null // No inventory
        },
        {
          productCode: 'PRODUCT-3',
          productName: 'Product 3',
          location: 'C1',
          lastStockTaking: '2024-01-05T10:00:00Z' // 10 days ago (oldest)
        },
        {
          productCode: 'PRODUCT-4',
          productName: 'Product 4',
          location: 'A2',
          lastStockTaking: null // No inventory
        },
        {
          productCode: 'PRODUCT-5',
          productName: 'Product 5',
          location: 'D1',
          lastStockTaking: '2024-01-14T10:00:00Z' // 1 day ago (newest)
        }
      ];

      // Mock three calls to catalog_GetCatalogList for Product, Goods, Set types
      mockApiClient.catalog_GetCatalogList
        .mockResolvedValueOnce({
          items: [mockInventoryItems[0], mockInventoryItems[1]], // First two items for Product type
          totalCount: 2,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[2], mockInventoryItems[3]], // Next two items for Goods type
          totalCount: 2,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[4]], // Last item for Set type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'lastInventoryDays', true // descending
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      const sortedItems = result.current.data?.items || [];
      
      // Expected order for descending:
      // 1. Items without inventory first (sorted by location: A2, B1)
      // 2. Items with inventory by oldest first (C1: 10 days, A1: 5 days, D1: 1 day)
      expect(sortedItems[0]?.productCode).toBe('PRODUCT-4'); // A2, no inventory
      expect(sortedItems[1]?.productCode).toBe('PRODUCT-2'); // B1, no inventory
      expect(sortedItems[2]?.productCode).toBe('PRODUCT-3'); // C1, 10 days ago (oldest)
      expect(sortedItems[3]?.productCode).toBe('PRODUCT-1'); // A1, 5 days ago
      expect(sortedItems[4]?.productCode).toBe('PRODUCT-5'); // D1, 1 day ago (newest)
    });

    it('should sort items without inventory by location when descending', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      
      const mockInventoryItems = [
        {
          productCode: 'PRODUCT-1',
          productName: 'Product 1',
          location: 'Z-Zone',
          lastStockTaking: null
        },
        {
          productCode: 'PRODUCT-2',
          productName: 'Product 2',
          location: 'A-Zone',
          lastStockTaking: null
        },
        {
          productCode: 'PRODUCT-3',
          productName: 'Product 3',
          location: 'M-Zone',
          lastStockTaking: null
        }
      ];

      // Mock three calls to catalog_GetCatalogList for Product, Goods, Set types
      mockApiClient.catalog_GetCatalogList
        .mockResolvedValueOnce({
          items: [mockInventoryItems[0]], // Z-Zone item for Product type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[1]], // A-Zone item for Goods type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[2]], // M-Zone item for Set type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'lastInventoryDays', true // descending
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      const sortedItems = result.current.data?.items || [];
      
      // Should be sorted by location alphabetically: A-Zone, M-Zone, Z-Zone
      expect(sortedItems[0]?.productCode).toBe('PRODUCT-2'); // A-Zone
      expect(sortedItems[1]?.productCode).toBe('PRODUCT-3'); // M-Zone
      expect(sortedItems[2]?.productCode).toBe('PRODUCT-1'); // Z-Zone
    });
  });

  describe('LastInventoryDays Sorting - Ascending', () => {
    it('should sort items with inventory first by newest, then items without inventory', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      
      const mockInventoryItems = [
        {
          productCode: 'PRODUCT-1',
          productName: 'Product 1',
          location: 'A1',
          lastStockTaking: '2024-01-10T10:00:00Z' // 5 days ago
        },
        {
          productCode: 'PRODUCT-2',
          productName: 'Product 2',
          location: 'B1',
          lastStockTaking: null // No inventory
        },
        {
          productCode: 'PRODUCT-3',
          productName: 'Product 3',
          location: 'C1',
          lastStockTaking: '2024-01-05T10:00:00Z' // 10 days ago (oldest)
        },
        {
          productCode: 'PRODUCT-4',
          productName: 'Product 4',
          location: 'A2',
          lastStockTaking: null // No inventory
        },
        {
          productCode: 'PRODUCT-5',
          productName: 'Product 5',
          location: 'D1',
          lastStockTaking: '2024-01-14T10:00:00Z' // 1 day ago (newest)
        }
      ];

      // Mock three calls to catalog_GetCatalogList for Product, Goods, Set types
      mockApiClient.catalog_GetCatalogList
        .mockResolvedValueOnce({
          items: [mockInventoryItems[0], mockInventoryItems[1]], // First two items for Product type
          totalCount: 2,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[2], mockInventoryItems[3]], // Next two items for Goods type
          totalCount: 2,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[4]], // Last item for Set type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'lastInventoryDays', false // ascending
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      const sortedItems = result.current.data?.items || [];
      
      // Expected order for ascending:
      // 1. Items with inventory by newest first (D1: 1 day, A1: 5 days, C1: 10 days)
      // 2. Items without inventory (sorted by location: A2, B1)
      expect(sortedItems[0]?.productCode).toBe('PRODUCT-5'); // D1, 1 day ago (newest)
      expect(sortedItems[1]?.productCode).toBe('PRODUCT-1'); // A1, 5 days ago
      expect(sortedItems[2]?.productCode).toBe('PRODUCT-3'); // C1, 10 days ago (oldest)
      expect(sortedItems[3]?.productCode).toBe('PRODUCT-4'); // A2, no inventory
      expect(sortedItems[4]?.productCode).toBe('PRODUCT-2'); // B1, no inventory
    });
  });

  describe('Single Product Type Sorting', () => {
    it('should use API sorting for single product type', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      
      const mockInventoryItems = [
        {
          productCode: 'PRODUCT-1',
          productName: 'Product 1',
          location: 'A1',
          lastStockTaking: '2024-01-10T10:00:00Z'
        },
        {
          productCode: 'PRODUCT-2',
          productName: 'Product 2',
          location: 'B1',
          lastStockTaking: '2024-01-14T10:00:00Z'
        }
      ];

      // Mock single call to catalog_GetCatalogList for specific product type
      mockApiClient.catalog_GetCatalogList.mockResolvedValue({
        items: mockInventoryItems,
        totalCount: 2,
        pageNumber: 1,
        pageSize: 20,
        totalPages: 1
      });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', 1, 1, 20, 'lastInventoryDays', true // specific type (ProductType.Product = 1)
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should call API with sorting parameters and return as-is (server-side sorting)
      expect(mockApiClient.catalog_GetCatalogList).toHaveBeenCalledWith(
        1, // ProductType.Product
        1, // pageNumber
        20, // pageSize
        'lastInventoryDays', // sortBy
        true, // sortDescending
        undefined, // productName
        undefined, // productCode
        undefined // searchTerm
      );

      const items = result.current.data?.items || [];
      expect(items).toHaveLength(2);
      // Items should be in the order returned by the API (server-side sorted)
      expect(items[0]?.productCode).toBe('PRODUCT-1');
      expect(items[1]?.productCode).toBe('PRODUCT-2');
    });
  });

  describe('Edge Cases', () => {
    it('should handle items with same lastStockTaking dates', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      const sameDate = '2024-01-10T10:00:00Z';
      const mockInventoryItems = [
        {
          productCode: 'PRODUCT-A',
          productName: 'Product A',
          location: 'Z1',
          lastStockTaking: sameDate
        },
        {
          productCode: 'PRODUCT-B',
          productName: 'Product B',
          location: 'A1',
          lastStockTaking: sameDate
        },
        {
          productCode: 'PRODUCT-C',
          productName: 'Product C',
          location: 'M1',
          lastStockTaking: sameDate
        }
      ];

      // Mock three calls to catalog_GetCatalogList for Product, Goods, Set types
      mockApiClient.catalog_GetCatalogList
        .mockResolvedValueOnce({
          items: [mockInventoryItems[0]], // Product A for Product type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[1]], // Product B for Goods type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[2]], // Product C for Set type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'lastInventoryDays', true
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      const items = result.current.data?.items || [];
      
      // When dates are the same, items should maintain stable sort
      // (implementation-dependent, but should be consistent)
      expect(items).toHaveLength(3);
      expect(items.every(item => item?.lastStockTaking === sameDate)).toBe(true);
    });

    it('should handle empty inventory list', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      
      // Mock three empty responses for Product, Goods, and Set types
      mockApiClient.catalog_GetCatalogList
        .mockResolvedValue({
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 0
        });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'lastInventoryDays', true
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      expect(result.current.data?.items).toEqual([]);
      expect(result.current.data?.totalCount).toBe(0);
    });

    it('should handle API errors gracefully', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      // Mock the first API call to throw an error (for Product type)
      mockApiClient.catalog_GetCatalogList.mockRejectedValue(new Error('Network error'));

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'lastInventoryDays', true
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isError).toBe(true);
      });

      expect(result.current.data).toBeUndefined();
      expect(result.current.error).toEqual(new Error('Network error'));
    });

    it('should handle invalid date strings', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      const mockInventoryItems = [
        {
          productCode: 'PRODUCT-1',
          productName: 'Product 1',
          location: 'A1',
          lastStockTaking: 'invalid-date'
        },
        {
          productCode: 'PRODUCT-2',
          productName: 'Product 2',
          location: 'B1',
          lastStockTaking: '2024-01-10T10:00:00Z'
        }
      ];

      // Mock three calls to catalog_GetCatalogList for Product, Goods, Set types
      mockApiClient.catalog_GetCatalogList
        .mockResolvedValueOnce({
          items: [mockInventoryItems[0]], // Invalid date item for Product type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[1]], // Valid date item for Goods type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [], // Empty for Set type
          totalCount: 0,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 0
        });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'lastInventoryDays', true
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should not crash and should handle invalid dates gracefully
      const items = result.current.data?.items || [];
      expect(items).toHaveLength(2);
    });
  });

  describe('Standard Sorting (Non-LastInventoryDays)', () => {
    it('should apply standard sorting for other columns', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
      const mockInventoryItems = [
        {
          productCode: 'PRODUCT-C',
          productName: 'Product C',
          location: 'A1',
          lastStockTaking: '2024-01-10T10:00:00Z'
        },
        {
          productCode: 'PRODUCT-A',
          productName: 'Product A',
          location: 'B1',
          lastStockTaking: '2024-01-14T10:00:00Z'
        },
        {
          productCode: 'PRODUCT-B',
          productName: 'Product B',
          location: 'C1',
          lastStockTaking: null
        }
      ];

      // Mock three calls to catalog_GetCatalogList for Product, Goods, Set types
      mockApiClient.catalog_GetCatalogList
        .mockResolvedValueOnce({
          items: [mockInventoryItems[0]], // Product C for Product type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[1]], // Product A for Goods type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        })
        .mockResolvedValueOnce({
          items: [mockInventoryItems[2]], // Product B for Set type
          totalCount: 1,
          pageNumber: 1,
          pageSize: 1000,
          totalPages: 1
        });

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '', 1, 20, 'productCode', false // Sort by productCode ascending
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      const sortedItems = result.current.data?.items || [];
      
      // Should be sorted by productCode alphabetically: A, B, C
      expect(sortedItems[0]?.productCode).toBe('PRODUCT-A');
      expect(sortedItems[1]?.productCode).toBe('PRODUCT-B');
      expect(sortedItems[2]?.productCode).toBe('PRODUCT-C');
    });
  });
});