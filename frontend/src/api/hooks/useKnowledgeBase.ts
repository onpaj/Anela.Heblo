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
  type UploadDocumentResponse2,
  type FileParameter,
  type GetFeedbackListResponse as GeneratedGetFeedbackListResponse,
} from '../generated/api-client';
import type {
  RagFeedbackLogSummary,
  RagFeedbackChunk,
  RagFeedbackStats,
} from '../../components/feedback/ragFeedbackTypes';

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

export const knowledgeBaseKeys = {
  all: [...QUERY_KEYS.knowledgeBase] as const,
  documents: (params?: GetDocumentsParams) =>
    [...QUERY_KEYS.knowledgeBase, 'documents', params ?? {}] as const,
  contentTypes: () => [...QUERY_KEYS.knowledgeBase, 'content-types'] as const,
  feedbackList: (params?: GetFeedbackListParams) =>
    [...QUERY_KEYS.knowledgeBase, 'feedback-list', params ?? {}] as const,
  chunkDetail: (chunkId: string) =>
    [...QUERY_KEYS.knowledgeBase, 'chunk-detail', chunkId] as const,
};

// ---- Feedback-list mapping ----
// Keeps the generated RAG DTOs from leaking into ragFeedbackTypes.ts consumers, which assume
// createdAt: string and null (not undefined) for absent optional fields.

const toLocalFeedbackChunk = (chunk: {
  chunkId?: string;
  documentId?: string;
  filename?: string;
  score?: number;
  content?: string;
}): RagFeedbackChunk => ({
  chunkId: chunk.chunkId ?? '',
  documentId: chunk.documentId ?? '',
  filename: chunk.filename ?? '',
  score: chunk.score ?? 0,
  content: chunk.content ?? '',
});

const toLocalFeedbackListResponse = (
  generated: GeneratedGetFeedbackListResponse,
): GetFeedbackListResponse => ({
  success: true,
  logs: (generated.logs ?? []).map((log) => ({
    id: log.id ?? '',
    question: log.question ?? '',
    answer: log.answer ?? '',
    expandedQuery: log.expandedQuery ?? null,
    systemPrompt: log.systemPrompt ?? '',
    retrievedChunks: (log.retrievedChunks ?? []).map(toLocalFeedbackChunk),
    topK: log.topK ?? 0,
    sourceCount: log.sourceCount ?? 0,
    durationMs: log.durationMs ?? 0,
    createdAt: log.createdAt ? log.createdAt.toISOString() : '',
    userId: log.userId ?? null,
    userName: log.userName ?? null,
    conversationId: log.conversationId ?? null,
    topic: log.topic ?? null,
    sentAnswer: log.sentAnswer ?? null,
    wasEdited: log.wasEdited ?? null,
    sentAt: log.sentAt ? log.sentAt.toISOString() : null,
    precisionScore: log.precisionScore ?? null,
    styleScore: log.styleScore ?? null,
    feedbackComment: log.feedbackComment ?? null,
    hasFeedback: log.hasFeedback ?? false,
  })),
  totalCount: generated.totalCount ?? 0,
  pageNumber: generated.pageNumber ?? 0,
  pageSize: generated.pageSize ?? 0,
  totalPages: generated.totalPages ?? 0,
  stats: {
    totalQuestions: generated.stats?.totalQuestions ?? 0,
    totalWithFeedback: generated.stats?.totalWithFeedback ?? 0,
    avgPrecisionScore: generated.stats?.avgPrecisionScore ?? null,
    avgStyleScore: generated.stats?.avgStyleScore ?? null,
  },
});

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
    }): Promise<UploadDocumentResponse2> => {
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
export const useKnowledgeBaseFeedbackListQuery = (params: GetFeedbackListParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.feedbackList(params),
    queryFn: async (): Promise<GetFeedbackListResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const generated = await apiClient.knowledgeBase_GetFeedbackList(
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDescending,
        params.hasFeedback,
        params.userId,
      );
      return toLocalFeedbackListResponse(generated);
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
