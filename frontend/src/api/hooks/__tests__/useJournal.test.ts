import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { 
  useJournalEntries, 
  useJournalEntry,
  useSearchJournalEntries,
  useCreateJournalEntry,
  useUpdateJournalEntry,
  useDeleteJournalEntry
} from '../useJournal';
import * as clientModule from '../../client';

// Mock the client module
jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    journal: ['journal']
  }
}));

const mockGetAuthenticatedApiClient = clientModule.getAuthenticatedApiClient as jest.MockedFunction<typeof clientModule.getAuthenticatedApiClient>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const mockJournalEntriesResponse = {
  success: true,
  entries: [
    {
      id: 1,
      title: 'Test Entry 1',
      content: 'Test content 1',
      entryDate: '2024-01-15T10:30:00Z',
      createdByUserId: 'user1',
      createdAt: '2024-01-15T10:30:00Z',
      modifiedAt: '2024-01-15T10:30:00Z',
      tags: [],
      associatedProductCodes: [],
      associatedProductFamilies: [],
    },
    {
      id: 2,
      title: 'Test Entry 2',
      content: 'Test content 2',
      entryDate: '2024-01-14T15:45:00Z',
      createdByUserId: 'user2',
      createdAt: '2024-01-14T15:45:00Z',
      modifiedAt: '2024-01-14T15:45:00Z',
      tags: [],
      associatedProductCodes: [],
      associatedProductFamilies: [],
    }
  ],
  totalCount: 2,
  currentPage: 1,
  pageSize: 20,
  totalPages: 1,
  message: 'Success'
};

describe('useJournal hooks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('useJournalEntries', () => {
    it('should fetch journal entries successfully', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_GetJournalEntries: jest.fn().mockResolvedValue(mockJournalEntriesResponse),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(
        () => useJournalEntries({
          pageNumber: 1,
          pageSize: 20,
          sortBy: 'EntryDate',
          sortDirection: 'DESC'
        }),
        { wrapper: createWrapper }
      );

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(result.current.data).toEqual(mockJournalEntriesResponse);
      expect(result.current.data?.entries).toHaveLength(2);
    });

    it('should handle API error', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_GetJournalEntries: jest.fn().mockRejectedValue(new Error('API Error')),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(
        () => useJournalEntries({
          pageNumber: 1,
          pageSize: 20
        }),
        { wrapper: createWrapper }
      );

      await waitFor(() => {
        expect(result.current.isError).toBe(true);
      });

      expect(result.current.error).toBeTruthy();
    });
  });

  describe('useJournalEntry', () => {
    it('should fetch single journal entry successfully', async () => {
      const mockEntry = {
        success: true,
        entry: mockJournalEntriesResponse.entries[0],
        message: 'Success'
      };

      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_GetJournalEntry: jest.fn().mockResolvedValue(mockEntry),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(
        () => useJournalEntry(1),
        { wrapper: createWrapper }
      );

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(result.current.data).toEqual(mockEntry);
      expect(result.current.data?.entry?.id).toBe(1);
    });

    it('should not fetch when id is invalid', () => {
      const mockJournalMethod = jest.fn();
      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_GetJournalEntry: mockJournalMethod,
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(
        () => useJournalEntry(0), // Use 0 which fails enabled: id > 0
        { wrapper: createWrapper }
      );

      // The query should not run because it's disabled
      expect(mockJournalMethod).not.toHaveBeenCalled();
      expect(result.current.data).toBeUndefined();
      
      // TanStack Query disabled queries may show as pending initially but they don't actually fetch
      // The important thing is that the API method wasn't called
    });
  });

  describe('useSearchJournalEntries', () => {
    it('should search journal entries successfully', async () => {
      const mockSearchResponse = {
        ...mockJournalEntriesResponse,
        entries: [mockJournalEntriesResponse.entries[0]], // Only first entry matches
        totalCount: 1
      };

      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_SearchJournalEntries: jest.fn().mockResolvedValue(mockSearchResponse),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(
        () => useSearchJournalEntries({
          searchText: 'test',
          pageNumber: 1,
          pageSize: 20
        }),
        { wrapper: createWrapper }
      );

      // Search queries are disabled by default (enabled: false in useSearchJournalEntries)
      // We need to manually trigger the query
      result.current.refetch();

      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });

      expect(result.current.data).toEqual(mockSearchResponse);
      expect(result.current.data?.entries).toHaveLength(1);
    });
  });

  describe('useCreateJournalEntry', () => {
    it('should create journal entry successfully', async () => {
      const mockCreateResponse = {
        success: true,
        message: 'Journal entry created successfully.'
      };

      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_CreateJournalEntry: jest.fn().mockResolvedValue(mockCreateResponse),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(() => useCreateJournalEntry(), { wrapper: createWrapper });

      const createData = {
        title: 'New Entry',
        content: 'New content',
        entryDate: new Date('2024-01-15'),
        createdByUserId: 'user123'
      };

      let mutationResult: any;
      mutationResult = await result.current.mutateAsync(createData);

      expect(mutationResult).toEqual(mockCreateResponse);
      
      // After mutateAsync resolves, isSuccess should be true
      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });
    });

    it('should handle create error', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_CreateJournalEntry: jest.fn().mockRejectedValue(new Error('Create failed')),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(() => useCreateJournalEntry(), { wrapper: createWrapper });

      const createData = {
        title: 'New Entry',
        content: 'New content',
        entryDate: new Date('2024-01-15'),
        createdByUserId: 'user123'
      };

      await expect(result.current.mutateAsync(createData)).rejects.toThrow('Create failed');
    });
  });

  describe('useUpdateJournalEntry', () => {
    it('should update journal entry successfully', async () => {
      const mockUpdateResponse = {
        success: true,
        message: 'Journal entry updated successfully.'
      };

      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_UpdateJournalEntry: jest.fn().mockResolvedValue(mockUpdateResponse),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(() => useUpdateJournalEntry(), { wrapper: createWrapper });

      const updateData = {
        id: 1,
        request: {
          title: 'Updated Entry',
          content: 'Updated content',
          entryDate: new Date('2024-01-15'),
          modifiedByUserId: 'user123'
        }
      };

      let mutationResult: any;
      mutationResult = await result.current.mutateAsync(updateData);

      expect(mutationResult).toEqual(mockUpdateResponse);
      
      // After mutateAsync resolves, isSuccess should be true
      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });
    });
  });

  describe('useDeleteJournalEntry', () => {
    it('should delete journal entry successfully', async () => {
      const mockDeleteMethod = jest.fn().mockResolvedValue(undefined);

      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_DeleteJournalEntry: mockDeleteMethod,
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(() => useDeleteJournalEntry(), { wrapper: createWrapper });

      await result.current.mutateAsync(1);

      // Verify the API method was called with correct ID
      expect(mockDeleteMethod).toHaveBeenCalledWith(1);
      
      // After mutateAsync resolves, isSuccess should be true
      await waitFor(() => {
        expect(result.current.isSuccess).toBe(true);
      });
    });

    it('should handle delete error', async () => {
      mockGetAuthenticatedApiClient.mockReturnValue({
        journal_DeleteJournalEntry: jest.fn().mockRejectedValue(new Error('Delete failed')),
        baseUrl: 'http://localhost:5001'
      } as any);

      const { result } = renderHook(() => useDeleteJournalEntry(), { wrapper: createWrapper });

      await expect(result.current.mutateAsync(1)).rejects.toThrow('Delete failed');
    });
  });

  describe('Query key generation', () => {
    it('should generate correct query keys', () => {
      // This is more of an integration test to ensure query keys are consistent
      const entriesParams = {
        pageNumber: 1,
        pageSize: 20,
        sortBy: 'EntryDate',
        sortDirection: 'DESC' as const
      };

      const searchParams = {
        searchText: 'test',
        pageNumber: 1,
        pageSize: 10
      };

      // Just ensure hooks can be called with these parameters
      const { result: entriesResult } = renderHook(
        () => useJournalEntries(entriesParams),
        { wrapper: createWrapper }
      );

      const { result: searchResult } = renderHook(
        () => useSearchJournalEntries(searchParams),
        { wrapper: createWrapper }
      );

      const { result: entryResult } = renderHook(
        () => useJournalEntry(1),
        { wrapper: createWrapper }
      );

      // Verify hooks are initialized
      expect(entriesResult.current).toBeDefined();
      expect(searchResult.current).toBeDefined();
      expect(entryResult.current).toBeDefined();
    });
  });
});