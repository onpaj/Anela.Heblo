import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useInventoryQuery } from '../useInventory';
import { getAuthenticatedApiClient } from '../client';

// Mock the API client
jest.mock('../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: {
    fetch: jest.fn(),
  },
};

(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);

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

  describe('LastInventoryDays Sorting - Descending (Default)', () => {
    it('should sort items without inventory first, then items with inventory by oldest first', async () => {
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

      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: mockInventoryItems,
          totalCount: 5,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 1
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

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
      expect(sortedItems[0].productCode).toBe('PRODUCT-4'); // A2, no inventory
      expect(sortedItems[1].productCode).toBe('PRODUCT-2'); // B1, no inventory
      expect(sortedItems[2].productCode).toBe('PRODUCT-3'); // C1, 10 days ago (oldest)
      expect(sortedItems[3].productCode).toBe('PRODUCT-1'); // A1, 5 days ago
      expect(sortedItems[4].productCode).toBe('PRODUCT-5'); // D1, 1 day ago (newest)
    });

    it('should sort items without inventory by location when descending', async () => {
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

      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: mockInventoryItems,
          totalCount: 3,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 1
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

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
      expect(sortedItems[0].productCode).toBe('PRODUCT-2'); // A-Zone
      expect(sortedItems[1].productCode).toBe('PRODUCT-3'); // M-Zone
      expect(sortedItems[2].productCode).toBe('PRODUCT-1'); // Z-Zone
    });
  });

  describe('LastInventoryDays Sorting - Ascending', () => {
    it('should sort items with inventory first by newest, then items without inventory', async () => {
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

      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: mockInventoryItems,
          totalCount: 5,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 1
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

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
      expect(sortedItems[0].productCode).toBe('PRODUCT-5'); // D1, 1 day ago (newest)
      expect(sortedItems[1].productCode).toBe('PRODUCT-1'); // A1, 5 days ago
      expect(sortedItems[2].productCode).toBe('PRODUCT-3'); // C1, 10 days ago (oldest)
      expect(sortedItems[3].productCode).toBe('PRODUCT-4'); // A2, no inventory
      expect(sortedItems[4].productCode).toBe('PRODUCT-2'); // B1, no inventory
    });
  });

  describe('Single Product Type Sorting', () => {
    it('should use API sorting for single product type', async () => {
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

      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: mockInventoryItems,
          totalCount: 2,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 1
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

      const { result } = renderHook(
        () => useInventoryQuery(
          '', '', '1', 1, 20, 'lastInventoryDays', true // specific type
        ),
        { wrapper: createWrapper() }
      );

      await waitFor(() => {
        expect(result.current.isLoading).toBe(false);
      });

      // Should call API with sorting parameters and return as-is (server-side sorting)
      expect(mockApiClient.http.fetch).toHaveBeenCalledWith(
        expect.stringContaining('sortBy=lastInventoryDays'),
        expect.any(Object)
      );
      expect(mockApiClient.http.fetch).toHaveBeenCalledWith(
        expect.stringContaining('sortDescending=true'),
        expect.any(Object)
      );

      const items = result.current.data?.items || [];
      expect(items).toHaveLength(2);
      // Items should be in the order returned by the API (server-side sorted)
      expect(items[0].productCode).toBe('PRODUCT-1');
      expect(items[1].productCode).toBe('PRODUCT-2');
    });
  });

  describe('Edge Cases', () => {
    it('should handle items with same lastStockTaking dates', async () => {
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

      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: mockInventoryItems,
          totalCount: 3,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 1
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

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
      expect(items.every(item => item.lastStockTaking === sameDate)).toBe(true);
    });

    it('should handle empty inventory list', async () => {
      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 0
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

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
      mockApiClient.http.fetch.mockRejectedValue(new Error('Network error'));

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

      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: mockInventoryItems,
          totalCount: 2,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 1
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

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

      const mockResponse = {
        ok: true,
        json: jest.fn().mockResolvedValue({
          items: mockInventoryItems,
          totalCount: 3,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 1
        })
      };

      mockApiClient.http.fetch.mockResolvedValue(mockResponse);

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
      expect(sortedItems[0].productCode).toBe('PRODUCT-A');
      expect(sortedItems[1].productCode).toBe('PRODUCT-B');
      expect(sortedItems[2].productCode).toBe('PRODUCT-C');
    });
  });
});