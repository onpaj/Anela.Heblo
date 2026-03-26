import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useMsal } from '@azure/msal-react';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import { shouldUseMockAuth } from '../../config/runtimeConfig';
import { mockAuthService } from '../../auth/mockAuth';

// ---- Types ----

export interface DocumentSummary {
  id: string;
  filename: string;
  status: string;
  contentType: string;
  createdAt: string;
  indexedAt: string | null;
}

export interface GetDocumentsParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  filenameFilter?: string;
  statusFilter?: string;
  contentTypeFilter?: string;
}

export interface GetDocumentsResponse {
  success: boolean;
  documents: DocumentSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface GetDocumentContentTypesResponse {
  success: boolean;
  contentTypes: string[];
}

export interface ChunkResult {
  chunkId: string;
  documentId: string;
  content: string;
  score: number;
  sourceFilename: string;
  sourcePath: string;
}

export interface SearchDocumentsResponse {
  success: boolean;
  chunks: ChunkResult[];
}

export interface SourceReference {
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface AskQuestionResponse {
  success: boolean;
  id: string | null;
  answer: string;
  sources: SourceReference[];
}

export interface SubmitFeedbackRequest {
  logId: string;
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface SubmitFeedbackResult {
  alreadySubmitted?: true;
}

export interface DeleteDocumentResponse {
  success: boolean;
}

export interface UploadDocumentResponse {
  success: boolean;
  document: DocumentSummary | null;
}

export interface FeedbackLogSummary {
  id: string;
  question: string;
  answer: string;
  topK: number;
  sourceCount: number;
  durationMs: number;
  createdAt: string;
  userId: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
  hasFeedback: boolean;
}

export interface FeedbackStatsDto {
  totalQuestions: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

export interface GetFeedbackListParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  hasFeedback?: boolean;
  userId?: string;
}

export interface GetFeedbackListResponse {
  success: boolean;
  logs: FeedbackLogSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  stats: FeedbackStatsDto;
}

// ---- Permission hooks ----

/**
 * Returns true when the current MSAL account has the knowledge_base_manager role.
 * Controls visibility of the Upload tab and delete buttons.
 */
export const useKnowledgeBaseUploadPermission = (): boolean => {
  const { accounts } = useMsal();

  // In mock auth mode, MSAL has no accounts — read roles from mockAuthService instead
  if (shouldUseMockAuth()) {
    const user = mockAuthService.getUser();
    return !!(Array.isArray(user?.roles) && user?.roles.includes('knowledge_base_manager'));
  }

  const account = accounts[0];
  if (!account) return false;
  const claims = account.idTokenClaims as Record<string, unknown> | undefined;
  const roles = claims?.['roles'];
  return Array.isArray(roles) && roles.includes('knowledge_base_manager');
};

// ---- Query key factory ----

export const knowledgeBaseKeys = {
  all: [...QUERY_KEYS.knowledgeBase] as const,
  documents: (params?: GetDocumentsParams) =>
    [...QUERY_KEYS.knowledgeBase, 'documents', params ?? {}] as const,
  contentTypes: () => [...QUERY_KEYS.knowledgeBase, 'content-types'] as const,
  feedbackList: (params?: GetFeedbackListParams) =>
    [...QUERY_KEYS.knowledgeBase, 'feedback-list', params ?? {}] as const,
};

// ---- Hooks ----

/**
 * Fetch paginated, filtered, sorted knowledge base documents.
 */
export const useKnowledgeBaseDocumentsQuery = (params: GetDocumentsParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.documents(params),
    queryFn: async (): Promise<GetDocumentsResponse> => {
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
      const relativeUrl = `/api/knowledgebase/documents${query ? `?${query}` : ''}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch documents: ${response.status}`);
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
export const useKnowledgeBaseContentTypesQuery = () => {
  return useQuery({
    queryKey: knowledgeBaseKeys.contentTypes(),
    queryFn: async (): Promise<GetDocumentContentTypesResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents/content-types`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch content types: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 10 * 60 * 1000,
    gcTime: 15 * 60 * 1000,
  });
};

/**
 * Semantic search over the knowledge base.
 */
export const useKnowledgeBaseSearchMutation = () => {
  return useMutation({
    mutationFn: async ({
      query,
      topK = 5,
    }: {
      query: string;
      topK?: number;
    }): Promise<SearchDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = '/api/knowledgebase/search';
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ query, topK }),
      });

      if (!response.ok) {
        throw new Error(`Search failed: ${response.status}`);
      }

      return response.json();
    },
  });
};

/**
 * Ask a question grounded in the knowledge base.
 */
export const useKnowledgeBaseAskMutation = () => {
  return useMutation({
    mutationFn: async ({
      question,
      topK = 5,
    }: {
      question: string;
      topK?: number;
    }): Promise<AskQuestionResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = '/api/knowledgebase/ask';
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ question, topK }),
      });

      if (!response.ok) {
        throw new Error(`Ask failed: ${response.status}`);
      }

      return response.json();
    },
  });
};

/**
 * Delete a document from the knowledge base.
 */
export const useDeleteKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (documentId: string): Promise<DeleteDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/knowledgebase/documents/${documentId}`;
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
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.all });
    },
  });
};

/**
 * Submit precision and style feedback for a Knowledge Base Ask response.
 * Returns { alreadySubmitted: true } on 409 instead of throwing.
 */
export const useSubmitFeedbackMutation = () => {
  return useMutation({
    mutationFn: async (payload: SubmitFeedbackRequest): Promise<SubmitFeedbackResult> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/feedback`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload),
      });

      if (response.status === 409) {
        return { alreadySubmitted: true };
      }

      if (!response.ok) {
        throw new Error(`Submit feedback failed: ${response.status}`);
      }

      return {};
    },
  });
};

/**
 * Upload a file to the knowledge base.
 * Sends multipart/form-data to POST /api/knowledgebase/documents/upload.
 * Invalidates the documents list on success.
 */
export const useUploadKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (file: File): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents/upload`;

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
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.all });
    },
  });
};

/**
 * Fetch paginated, filtered, sorted feedback logs for the Feedback Browser.
 * Only accessible to knowledge_base_manager role.
 */
export const useKnowledgeBaseFeedbackListQuery = (params: GetFeedbackListParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.feedbackList(params),
    queryFn: async (): Promise<GetFeedbackListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();

      if (params.pageNumber !== undefined)
        searchParams.append('pageNumber', params.pageNumber.toString());
      if (params.pageSize !== undefined)
        searchParams.append('pageSize', params.pageSize.toString());
      if (params.sortBy) searchParams.append('sortBy', params.sortBy);
      if (params.sortDescending !== undefined)
        searchParams.append('sortDescending', params.sortDescending.toString());
      if (params.hasFeedback !== undefined)
        searchParams.append('hasFeedback', params.hasFeedback.toString());
      if (params.userId) searchParams.append('userId', params.userId);

      const query = searchParams.toString();
      const relativeUrl = `/api/knowledgebase/feedback/list${query ? `?${query}` : ''}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch feedback list: ${response.status}`);
      }

      return response.json();
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};
