import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

// ---- Types ----

export interface DocumentSummary {
  id: string;
  filename: string;
  status: string;
  contentType: string;
  createdAt: string;
  indexedAt: string | null;
}

export interface GetDocumentsResponse {
  success: boolean;
  documents: DocumentSummary[];
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
  answer: string;
  sources: SourceReference[];
}

export interface DeleteDocumentResponse {
  success: boolean;
}

// ---- Query key factory ----

export const knowledgeBaseKeys = {
  all: [...QUERY_KEYS.knowledgeBase] as const,
  documents: () => [...QUERY_KEYS.knowledgeBase, 'documents'] as const,
};

// ---- Hooks ----

/**
 * Fetch all indexed knowledge base documents.
 */
export const useKnowledgeBaseDocumentsQuery = () => {
  return useQuery({
    queryKey: knowledgeBaseKeys.documents(),
    queryFn: async (): Promise<GetDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = '/api/knowledgebase/documents';
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
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.documents() });
    },
  });
};
