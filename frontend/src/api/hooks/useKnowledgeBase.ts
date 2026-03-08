import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

// ---- Types ----

export interface DocumentSummary {
  id: string;
  filename: string;
  status: 'processing' | 'indexed' | 'failed';
  contentType: string;
  createdAt: string;
  indexedAt?: string;
}

export interface ChunkResult {
  chunkId: string;
  documentId: string;
  content: string;
  score: number;
  sourceFilename: string;
  sourcePath: string;
}

export interface SearchResponse {
  chunks: ChunkResult[];
}

export interface SourceReference {
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface AskResponse {
  answer: string;
  sources: SourceReference[];
}

// ---- Query key factory ----

export const knowledgeBaseKeys = {
  documents: ['knowledgeBase', 'documents'] as const,
};

// ---- Hooks ----

export const useKnowledgeBaseDocumentsQuery = () => {
  return useQuery({
    queryKey: knowledgeBaseKeys.documents,
    queryFn: async (): Promise<DocumentSummary[]> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
    staleTime: 30 * 1000,
  });
};

export const useKnowledgeBaseSearchMutation = () => {
  return useMutation({
    mutationFn: async ({ query, topK = 5 }: { query: string; topK?: number }): Promise<SearchResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/search`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query, topK }),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
  });
};

export const useKnowledgeBaseAskMutation = () => {
  return useMutation({
    mutationFn: async ({ question, topK = 5 }: { question: string; topK?: number }): Promise<AskResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/ask`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, topK }),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
  });
};

export const useDeleteKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (documentId: string): Promise<void> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents/${documentId}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'DELETE' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.documents });
    },
  });
};
