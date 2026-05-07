import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// ---- Types ----

export interface LeafletDocumentSummary {
  id: string;
  filename: string;
  status: string;
  contentType: string;
  ingestedAt: string;
  indexedAt: string | null;
  firstChunkId: string | null;
}

export interface GetLeafletDocumentsParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  filenameFilter?: string;
  statusFilter?: string;
  contentTypeFilter?: string;
}

export interface GetLeafletDocumentsResponse {
  success: boolean;
  documents: LeafletDocumentSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface GetLeafletContentTypesResponse {
  success: boolean;
  contentTypes: string[];
}

export interface GetLeafletChunkDetailResponse {
  success: boolean;
  chunkId: string;
  documentId: string;
  filename: string;
  documentType: string;
  indexedAt: string | null;
  chunkIndex: number;
  summary: string;
  content: string;
  sourcePath?: string;
}

export interface DeleteLeafletDocumentResponse {
  success: boolean;
}

export interface UploadLeafletDocumentResponse {
  success: boolean;
  document: LeafletDocumentSummary | null;
}

export interface SubmitLeafletFeedbackResult {
  success: boolean;
  errorCode?: string | null;
  alreadySubmitted?: boolean;
}

export interface LeafletFeedbackListParams {
  hasFeedback?: boolean;
  userId?: string;
  sortBy?: string;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface LeafletFeedbackSummary {
  id: string;
  topic: string;
  audience: string;
  length: string;
  finalMarkdown: string;
  kbSourceCount: number;
  leafletSourceCount: number;
  durationMs: number;
  createdAt: string;
  userId: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
  hasFeedback: boolean;
}

export interface LeafletFeedbackListResponse {
  items: LeafletFeedbackSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  stats: {
    totalGenerations: number;
    totalWithFeedback: number;
    avgPrecisionScore: number | null;
    avgStyleScore: number | null;
  };
}

// ---- Query key factory ----

export const leafletKeys = {
  all: [...QUERY_KEYS.leaflet] as const,
  documents: (params?: GetLeafletDocumentsParams) =>
    [...QUERY_KEYS.leaflet, 'documents', params ?? {}] as const,
  contentTypes: () => [...QUERY_KEYS.leaflet, 'content-types'] as const,
  chunkDetail: (chunkId: string) =>
    [...QUERY_KEYS.leaflet, 'chunk-detail', chunkId] as const,
  feedbackList: (params?: LeafletFeedbackListParams) =>
    [...QUERY_KEYS.leaflet, 'feedback-list', params ?? {}] as const,
  generation: (id: string) =>
    [...QUERY_KEYS.leaflet, 'generation', id] as const,
};

// ---- Hooks ----

/**
 * Fetch paginated, filtered, sorted leaflet documents.
 */
export const useLeafletDocumentsQuery = (params: GetLeafletDocumentsParams = {}) => {
  return useQuery({
    queryKey: leafletKeys.documents(params),
    queryFn: async (): Promise<GetLeafletDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();

      if (params.pageNumber !== undefined)
        searchParams.append('pageNumber', params.pageNumber.toString());
      if (params.pageSize !== undefined)
        searchParams.append('pageSize', params.pageSize.toString());
      if (params.sortBy) searchParams.append('sortBy', params.sortBy);
      if (params.sortDescending !== undefined)
        searchParams.append('sortDescending', params.sortDescending.toString());
      if (params.filenameFilter) searchParams.append('filenameFilter', params.filenameFilter);
      if (params.statusFilter) searchParams.append('statusFilter', params.statusFilter);
      if (params.contentTypeFilter)
        searchParams.append('contentTypeFilter', params.contentTypeFilter);

      const query = searchParams.toString();
      const relativeUrl = `/api/leaflet/documents${query ? `?${query}` : ''}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch leaflet documents: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};

/**
 * Fetch distinct content types for the filter dropdown.
 */
export const useLeafletContentTypesQuery = () => {
  return useQuery({
    queryKey: leafletKeys.contentTypes(),
    queryFn: async (): Promise<GetLeafletContentTypesResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/leaflet/documents/content-types`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch leaflet content types: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 10 * 60 * 1000,
    gcTime: 15 * 60 * 1000,
  });
};

/**
 * Fetch full chunk content and document metadata by chunk ID.
 * Only fires when chunkId is non-null.
 */
export const useLeafletChunkDetailQuery = (chunkId: string | null) => {
  return useQuery({
    queryKey: leafletKeys.chunkDetail(chunkId ?? ''),
    queryFn: async (): Promise<GetLeafletChunkDetailResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/leaflet/chunks/${chunkId}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch leaflet chunk detail: ${response.status}`);
      }

      return response.json();
    },
    enabled: !!chunkId,
    staleTime: 10 * 60 * 1000,
    gcTime: 15 * 60 * 1000,
  });
};

/**
 * Delete a document from the leaflet document store.
 */
export const useDeleteLeafletDocumentMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (documentId: string): Promise<DeleteLeafletDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/leaflet/documents/${documentId}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'DELETE',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Delete failed: ${response.status}`);
      }

      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: leafletKeys.all });
    },
  });
};

/**
 * Upload a file to the leaflet document store.
 * Sends multipart/form-data to POST /api/leaflet/documents/upload.
 * Invalidates the documents list on success.
 */
export const useUploadLeafletDocumentMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ file }: { file: File }): Promise<UploadLeafletDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/leaflet/documents/upload`;

      const formData = new FormData();
      formData.append('file', file);

      // Do NOT set Content-Type header — browser sets it with multipart boundary automatically
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error(`Upload failed: ${response.status}`);
      }

      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: leafletKeys.all });
    },
  });
};

/**
 * Submit precision/style feedback for a leaflet generation.
 * HTTP 409 is treated as already-submitted (not an error throw).
 */
export const useSubmitLeafletFeedbackMutation = () => {
  return useMutation({
    mutationFn: async (params: {
      generationId: string;
      precisionScore: number;
      styleScore: number;
      comment?: string;
    }): Promise<SubmitLeafletFeedbackResult> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/leaflet/feedback`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(params),
      });

      if (response.status === 409) {
        return { success: false, alreadySubmitted: true };
      }

      if (!response.ok) {
        throw new Error(`Submit feedback failed: ${response.status}`);
      }

      return response.json();
    },
  });
};

/**
 * Fetch paginated leaflet generation feedback list (admin only).
 */
export const useLeafletFeedbackListQuery = (params: LeafletFeedbackListParams = {}) => {
  return useQuery({
    queryKey: leafletKeys.feedbackList(params),
    queryFn: async (): Promise<LeafletFeedbackListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();

      if (params.hasFeedback !== undefined)
        searchParams.append('hasFeedback', params.hasFeedback.toString());
      if (params.userId) searchParams.append('userId', params.userId);
      if (params.sortBy) searchParams.append('sortBy', params.sortBy);
      if (params.sortDescending !== undefined)
        searchParams.append('sortDescending', params.sortDescending.toString());
      if (params.pageNumber !== undefined)
        searchParams.append('pageNumber', params.pageNumber.toString());
      if (params.pageSize !== undefined)
        searchParams.append('pageSize', params.pageSize.toString());

      const query = searchParams.toString();
      const fullUrl = `${(apiClient as any).baseUrl}/api/leaflet/feedback/list${query ? `?${query}` : ''}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) throw new Error(`Failed to fetch feedback list: ${response.status}`);
      return response.json() as Promise<LeafletFeedbackListResponse>;
    },
    staleTime: 30_000,
  });
};
