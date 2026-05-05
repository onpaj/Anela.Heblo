import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useMsal } from '@azure/msal-react';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import { shouldUseMockAuth } from '../../config/runtimeConfig';
import { mockAuthService } from '../../auth/mockAuth';

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
}

export interface DeleteLeafletDocumentResponse {
  success: boolean;
}

export interface UploadLeafletDocumentResponse {
  success: boolean;
  document: LeafletDocumentSummary | null;
}

export interface SubmitLeafletFeedbackPayload {
  generationId: string;
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface SubmitLeafletFeedbackResult {
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

export interface LeafletFeedbackStatsDto {
  totalGenerations: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

export interface LeafletGenerationSummary {
  id: string;
  topic: string;
  audience: string;
  length: string;
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
  success: boolean;
  logs: LeafletGenerationSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  stats: LeafletFeedbackStatsDto;
}

// ---- Permission hook ----

/**
 * Returns true when the current MSAL account has the leaflet_manager role.
 * Controls visibility of the Upload tab and delete buttons.
 */
export const useLeafletUploadPermission = (): boolean => {
  const { accounts } = useMsal();

  if (shouldUseMockAuth()) {
    const user = mockAuthService.getUser();
    return !!(Array.isArray(user?.roles) && user?.roles.includes('leaflet_manager'));
  }

  const account = accounts[0];
  if (!account) return false;
  const claims = account.idTokenClaims as Record<string, unknown> | undefined;
  const roles = claims?.['roles'];
  return Array.isArray(roles) && roles.includes('leaflet_manager');
};

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

// ---- Leaflet Feedback Hooks ----

/**
 * Submit feedback (precision and style scores) for a leaflet generation.
 * Returns 409 if feedback was already submitted for this generation.
 */
export const useSubmitLeafletFeedbackMutation = () => {
  return useMutation({
    mutationFn: async (payload: SubmitLeafletFeedbackPayload): Promise<SubmitLeafletFeedbackResult> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/leaflet/feedback`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload),
      });

      if (response.status === 409) {
        return { alreadySubmitted: true };
      }

      if (!response.ok) {
        throw new Error(`Submit leaflet feedback failed: ${response.status}`);
      }

      return {};
    },
  });
};

/**
 * Fetch paginated, filtered leaflet generation logs with feedback status.
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
      const relativeUrl = `/api/leaflet/feedback/list${query ? `?${query}` : ''}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch leaflet feedback list: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 30_000,
  });
};
