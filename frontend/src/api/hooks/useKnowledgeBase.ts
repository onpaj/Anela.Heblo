import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  SearchDocumentsRequest,
  AskQuestionRequest,
  SubmitFeedbackRequest,
  type ISubmitFeedbackRequest,
  type GetDocumentsResponse,
  type GetDocumentContentTypesResponse,
  type SearchDocumentsResponse,
  type AskQuestionResponse,
  type GetChunkDetailResponse,
  type DeleteDocumentResponse,
  type KnowledgeBaseUploadDocumentResponse,
  type FileParameter,
  GetFeedbackListResponse as GeneratedGetFeedbackListResponse,
} from '../generated/api-client';
import type {
  RagFeedbackLogSummary,
  RagFeedbackStats,
  TagCount,
} from '../../components/feedback/ragFeedbackTypes';
import { toLocalFeedbackListResponse } from '../../components/feedback/ragFeedbackMapping';

// ---- Types ----

export interface GetDocumentsParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  filenameFilter?: string;
  statusFilter?: string;
  contentTypeFilter?: string;
}

export interface SubmitFeedbackResult {
  alreadySubmitted?: true;
}

export type DocumentType = 'KnowledgeBase' | 'Conversation';

// The KB feedback list now returns the unified RAG feedback shape (question/answer/scores plus the
// eval fields: retrieved chunks + scores, expanded query, rendered prompt, and — for Smartsupp —
// the sent answer / edited flag). Aliased so existing imports keep working.
export type FeedbackLogSummary = RagFeedbackLogSummary;
export type FeedbackStatsDto = RagFeedbackStats;

export interface GetFeedbackListParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  hasFeedback?: boolean;
  userId?: string;
  /** yyyy-MM-dd */
  dateFrom?: string;
  /** yyyy-MM-dd */
  dateTo?: string;
  problemTag?: string;
  productTag?: string;
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

// ---- Query key factory ----

export interface TagStatsParams {
  /** yyyy-MM-dd */
  dateFrom?: string;
  /** yyyy-MM-dd */
  dateTo?: string;
}

export interface QuestionTagStats {
  problemTags: TagCount[];
  productTags: TagCount[];
  recommendedProducts: TagCount[];
}

export const knowledgeBaseKeys = {
  all: [...QUERY_KEYS.knowledgeBase] as const,
  documents: (params?: GetDocumentsParams) =>
    [...QUERY_KEYS.knowledgeBase, 'documents', params ?? {}] as const,
  contentTypes: () => [...QUERY_KEYS.knowledgeBase, 'content-types'] as const,
  feedbackList: (params?: GetFeedbackListParams) =>
    [...QUERY_KEYS.knowledgeBase, 'feedback-list', params ?? {}] as const,
  tagStats: (params: TagStatsParams) =>
    [...QUERY_KEYS.knowledgeBase, 'tag-stats', params] as const,
  chunkDetail: (chunkId: string) =>
    [...QUERY_KEYS.knowledgeBase, 'chunk-detail', chunkId] as const,
};

// ---- Hooks ----

/**
 * Fetch paginated, filtered, sorted knowledge base documents.
 */
export const useKnowledgeBaseDocumentsQuery = (params: GetDocumentsParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.documents(params),
    queryFn: (): Promise<GetDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.knowledgeBase_GetDocuments(
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDescending,
        params.filenameFilter,
        params.statusFilter,
        params.contentTypeFilter,
      );
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
    queryFn: (): Promise<GetDocumentContentTypesResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.knowledgeBase_GetDocumentContentTypes();
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
    mutationFn: ({
      query,
      topK = 5,
    }: {
      query: string;
      topK?: number;
    }): Promise<SearchDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.knowledgeBase_Search(new SearchDocumentsRequest({ query, topK }));
    },
  });
};

/**
 * Fetch full chunk content and document metadata by chunk ID.
 * Only fires when chunkId is non-null.
 */
export const useChunkDetailQuery = (chunkId: string | null) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.chunkDetail(chunkId ?? ''),
    queryFn: (): Promise<GetChunkDetailResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.knowledgeBase_GetChunkDetail(chunkId!);
    },
    enabled: !!chunkId,
    staleTime: 10 * 60 * 1000,
    gcTime: 15 * 60 * 1000,
  });
};

/**
 * Ask a question grounded in the knowledge base.
 */
export const useKnowledgeBaseAskMutation = () => {
  return useMutation({
    mutationFn: ({
      question,
      topK = 5,
    }: {
      question: string;
      topK?: number;
    }): Promise<AskQuestionResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.knowledgeBase_Ask(new AskQuestionRequest({ question, topK }));
    },
  });
};

/**
 * Delete a document from the knowledge base.
 */
export const useDeleteKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (documentId: string): Promise<DeleteDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.knowledgeBase_DeleteDocument(documentId);
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
    mutationFn: async (payload: ISubmitFeedbackRequest): Promise<SubmitFeedbackResult> => {
      const apiClient = getAuthenticatedApiClient();
      try {
        await apiClient.knowledgeBase_SubmitFeedback(new SubmitFeedbackRequest(payload));
        return {};
      } catch (e: unknown) {
        const err = e as { status?: number };
        if (err.status === 409) {
          return { alreadySubmitted: true };
        }
        throw e;
      }
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
    mutationFn: ({
      file,
      documentType,
    }: {
      file: File;
      documentType: DocumentType;
    }): Promise<KnowledgeBaseUploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fileParameter: FileParameter = { data: file, fileName: file.name };
      return apiClient.knowledgeBase_UploadDocument(fileParameter, documentType);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.all });
    },
  });
};

/**
 * Fetch paginated, filtered, sorted feedback logs for the Feedback Browser.
 * Gated in the UI by the customer.knowledge_base.write permission.
 */
// The generated client serializes DateOnly query params with toISOString(), which ASP.NET won't bind
// to DateOnly (and which is a day early for a local-midnight Date in CEST). Build the query by hand.
export const buildFeedbackListQuery = (params: GetFeedbackListParams | TagStatsParams): string => {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return;
    search.append(key, String(value));
  });
  return search.toString();
};

const fetchKnowledgeBaseJson = async (relativeUrl: string): Promise<unknown> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`HTTP error! status: ${response.status}`);
  }
  return response.json();
};

export const useKnowledgeBaseFeedbackListQuery = (params: GetFeedbackListParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.feedbackList(params),
    queryFn: async (): Promise<GetFeedbackListResponse> => {
      const query = buildFeedbackListQuery(params);
      const json = await fetchKnowledgeBaseJson(`/api/KnowledgeBase/feedback/list${query ? `?${query}` : ''}`);
      // fromJS revives createdAt/sentAt into Date objects, which toLocalFeedbackListResponse expects.
      return toLocalFeedbackListResponse(GeneratedGetFeedbackListResponse.fromJS(json));
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

export const useKnowledgeBaseTagStatsQuery = (params: TagStatsParams = {}, options: { enabled?: boolean } = {}) => {
  return useQuery({
    enabled: options.enabled ?? true,
    queryKey: knowledgeBaseKeys.tagStats(params),
    queryFn: async (): Promise<QuestionTagStats> => {
      const query = buildFeedbackListQuery(params);
      const json = (await fetchKnowledgeBaseJson(
        `/api/KnowledgeBase/feedback/tag-stats${query ? `?${query}` : ''}`,
      )) as Partial<Record<keyof QuestionTagStats, Partial<TagCount>[]>>;
      const toTagCounts = (items?: Partial<TagCount>[]): TagCount[] =>
        (items ?? []).map((i) => ({ tag: i.tag ?? '', label: i.label ?? i.tag ?? '', count: i.count ?? 0 }));
      return {
        problemTags: toTagCounts(json.problemTags),
        productTags: toTagCounts(json.productTags),
        recommendedProducts: toTagCounts(json.recommendedProducts),
      };
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

// Re-export types for convenience
export type {
  DocumentSummary,
  ChunkResult,
  SourceReference,
} from '../generated/api-client';
