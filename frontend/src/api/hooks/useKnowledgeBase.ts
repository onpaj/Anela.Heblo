import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useMsal } from '@azure/msal-react';
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

export interface UploadDocumentResponse {
  success: boolean;
  document: DocumentSummary | null;
}

// ---- Permission hooks ----

/**
 * Returns true when the current MSAL account has the knowledge_base_manager role.
 * Controls visibility of the Upload tab and delete buttons.
 */
export const useKnowledgeBaseUploadPermission = (): boolean => {
  const { accounts } = useMsal();
  const account = accounts[0];
  if (!account) return false;
  const claims = account.idTokenClaims as Record<string, unknown> | undefined;
  const roles = claims?.['roles'];
  return Array.isArray(roles) && roles.includes('knowledge_base_manager');
};

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
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.documents() });
    },
  });
};
