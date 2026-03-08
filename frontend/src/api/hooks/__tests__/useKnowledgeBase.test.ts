import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import {
  useKnowledgeBaseDocumentsQuery,
  useKnowledgeBaseSearchMutation,
  useKnowledgeBaseAskMutation,
  useDeleteKnowledgeBaseDocumentMutation,
} from '../useKnowledgeBase';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    knowledgeBase: ['knowledge-base'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const mockFetchResponse = (data: unknown, ok = true) => ({
  ok,
  json: jest.fn().mockResolvedValue(data),
  status: ok ? 200 : 500,
});

describe('useKnowledgeBase hooks', () => {
  let mockHttp: { fetch: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockHttp = { fetch: jest.fn() };
    mockGetAuthenticatedApiClient.mockReturnValue({
      baseUrl: 'http://localhost:5001',
      http: mockHttp,
    } as any);
  });

  describe('useKnowledgeBaseDocumentsQuery', () => {
    it('fetches documents and returns correct shape', async () => {
      const mockData = {
        success: true,
        documents: [
          {
            id: 'doc-1',
            filename: 'safety-data.pdf',
            status: 'indexed',
            contentType: 'application/pdf',
            createdAt: '2026-03-01T10:00:00Z',
            indexedAt: '2026-03-01T10:05:00Z',
          },
        ],
      };
      mockHttp.fetch.mockResolvedValue(mockFetchResponse(mockData));

      const { result } = renderHook(() => useKnowledgeBaseDocumentsQuery(), {
        wrapper: createWrapper,
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockHttp.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/knowledgebase/documents',
        expect.objectContaining({ method: 'GET' }),
      );
      expect(result.current.data?.documents).toHaveLength(1);
      expect(result.current.data?.documents[0].filename).toBe('safety-data.pdf');
    });
  });

  describe('useKnowledgeBaseSearchMutation', () => {
    it('sends correct POST body and returns chunks', async () => {
      const mockData = {
        success: true,
        chunks: [
          {
            chunkId: 'chunk-1',
            documentId: 'doc-1',
            content: 'Max phenoxyethanol 1.0% per EU regulation',
            score: 0.92,
            sourceFilename: 'EU_reg.pdf',
            sourcePath: '/archived/EU_reg.pdf',
          },
        ],
      };
      mockHttp.fetch.mockResolvedValue(mockFetchResponse(mockData));

      const { result } = renderHook(() => useKnowledgeBaseSearchMutation(), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        result.current.mutate({ query: 'phenoxyethanol limit', topK: 3 });
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockHttp.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/knowledgebase/search',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ query: 'phenoxyethanol limit', topK: 3 }),
        }),
      );
      expect(result.current.data?.chunks).toHaveLength(1);
      expect(result.current.data?.chunks[0].score).toBe(0.92);
    });
  });

  describe('useKnowledgeBaseAskMutation', () => {
    it('sends question and returns answer with sources', async () => {
      const mockData = {
        success: true,
        answer: 'The maximum allowed concentration is 1.0%.',
        sources: [
          {
            documentId: 'doc-1',
            filename: 'EU_reg.pdf',
            excerpt: 'Max phenoxyethanol 1.0% per EU regulation',
            score: 0.95,
          },
        ],
      };
      mockHttp.fetch.mockResolvedValue(mockFetchResponse(mockData));

      const { result } = renderHook(() => useKnowledgeBaseAskMutation(), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        result.current.mutate({ question: 'What is the max phenoxyethanol?', topK: 5 });
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data?.answer).toBe('The maximum allowed concentration is 1.0%.');
      expect(result.current.data?.sources).toHaveLength(1);
    });
  });

  describe('useDeleteKnowledgeBaseDocumentMutation', () => {
    it('sends DELETE request with document id', async () => {
      const mockData = { success: true };
      mockHttp.fetch.mockResolvedValue(mockFetchResponse(mockData));

      const { result } = renderHook(() => useDeleteKnowledgeBaseDocumentMutation(), {
        wrapper: createWrapper,
      });

      await waitFor(() => {
        result.current.mutate('doc-abc-123');
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(mockHttp.fetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/knowledgebase/documents/doc-abc-123',
        expect.objectContaining({ method: 'DELETE' }),
      );
    });
  });
});
