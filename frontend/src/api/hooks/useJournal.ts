import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import type {
  CreateJournalEntryRequest,
  UpdateJournalEntryRequest,
  CreateJournalTagRequest
} from '../generated/api-client';

interface JournalEntriesParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: string;
}

interface SearchJournalParams {
  searchText?: string;
  dateFrom?: Date;
  dateTo?: Date;
  productCodes?: string[];
  productCodePrefixes?: string[];
  tagIds?: number[];
  createdByUserId?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: string;
}

export const useJournalEntries = (params: JournalEntriesParams = {}) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, 'entries', params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_GetJournalEntries(
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDirection
      );
    }
  });
};

export const useSearchJournalEntries = (params: SearchJournalParams) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, 'search', params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_SearchJournalEntries(
        params.searchText || undefined,
        params.dateFrom || undefined,
        params.dateTo || undefined,
        params.productCodes || undefined,
        params.productCodePrefixes || undefined,
        params.tagIds || undefined,
        params.createdByUserId || undefined,
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDirection
      );
    },
    enabled: false // Only run when explicitly called
  });
};

export const useJournalEntry = (id: number) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, 'entry', id],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_GetJournalEntry(id);
    },
    enabled: id > 0
  });
};

export const useCreateJournalEntry = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: CreateJournalEntryRequest) => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_CreateJournalEntry(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'entries'] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'search'] });
    }
  });
};

export const useUpdateJournalEntry = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async ({ id, request }: { id: number; request: UpdateJournalEntryRequest }) => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_UpdateJournalEntry(id, request);
    },
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'entries'] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'search'] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'entry', id] });
    }
  });
};

export const useDeleteJournalEntry = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (id: number) => {
      const client = await getAuthenticatedApiClient();
      await client.journal_DeleteJournalEntry(id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'entries'] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'search'] });
    }
  });
};

export const useJournalTags = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, 'tags'],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_GetJournalTags();
    }
  });
};

export const useCreateJournalTag = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: CreateJournalTagRequest) => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_CreateJournalTag(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.journal, 'tags'] });
    }
  });
};